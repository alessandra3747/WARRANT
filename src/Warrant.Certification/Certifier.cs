using Warrant.Application;
using Warrant.Domain;

namespace Warrant.Certification;

public record SkipDecision(bool Skip, string? PriorContentHash);
public record ContractSnapshot(string ContractId, int Version, string? ContentHash, string Verdict, DateTimeOffset ValidUntil);

public interface ICertifier
{
    Task<string> CertifyAsync(Guid streamId, Guid cycleId, TaskProfile profile, CancellationToken ct = default);
    Task<string> RenewAsync(Guid streamId, TaskProfile profile, CancellationToken ct = default);
    Task<SkipDecision> ShouldSkipAsync(string contentHash, string agentId, string logicalName, CancellationToken ct = default);
    Task CertifyErrorAsync(Guid streamId, string logicalName, TaskProfile profile, string error, CancellationToken ct = default);
}

public interface IContractProjection
{
    Task UpsertAsync(string contractId, object payload, string jws, CancellationToken ct = default);
    Task<ContractSnapshot?> GetLatestByAssetAsync(string asset, string agentId, CancellationToken ct = default);
    Task RenewTtlAsync(string contractId, DateTimeOffset newValidUntil, CancellationToken ct = default);
    Task RevokeAsync(string asset, string agentId, CancellationToken ct = default);
}

public sealed class Certifier(IEventStore events, IContractSigner signer, IContractProjection projection,
    IWaiverStore waivers, DecisionPolicy policy, INotifier notifier, IMetrics metrics) : ICertifier
{
    private readonly IEventStore _events = events;
    private readonly IContractSigner _signer = signer;
    private readonly IContractProjection _projection = projection;
    private readonly IWaiverStore _waivers = waivers;
    private readonly DecisionPolicy _policy = policy;
    private readonly INotifier _notifier = notifier;
    private readonly IMetrics _metrics = metrics;

    public async Task<string> CertifyAsync(Guid streamId, Guid cycleId, TaskProfile profile, CancellationToken ct = default)
    {
        var agg = CertificationAggregate.Rehydrate(await _events.ReadCycleAsync(streamId, cycleId, ct));

        var asset = agg.LogicalName ?? profile.RequiredEntities.FirstOrDefault() ?? "unknown";
        var agentId = profile.AgentId.ToString();

        var active = await _waivers.GetActiveAsync(asset, agentId, ct);
        if (active.Count > 0)
        {
            agg.RemoveWaived(f => active.Any(w => string.Equals(w.Category, f.Category, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(w.Field) || string.Equals(w.Field, f.Field, StringComparison.OrdinalIgnoreCase)))
            );
        }

        var verdict = agg.Decide(profile, _policy);

        var contractId = $"WRNT-{streamId:N}";
        var prior = await _projection.GetLatestByAssetAsync(asset, agentId, ct);
        var version = (prior?.Version ?? 0) + 1;
        var validUntil = DateTimeOffset.UtcNow.AddHours(profile.ContractTtlHours);

        var provenance = agg.Signals.GroupBy(s => s.Mode).ToDictionary(g => g.Key, g => g.Select(x => x.Type).Distinct().ToArray());

        var payload = new
        {
            contractId, asset, agentId,
            taskPurpose = profile.Purpose, legalBasis = profile.LegalBasis,
            verdict = verdict.Kind.ToString(), hallucinationRisk = verdict.HallucinationRisk,
            findings = verdict.Findings, signalProvenance = provenance,
            contentHash = agg.ContentHash,
            validFrom = DateTimeOffset.UtcNow, validUntil, version
        };

        var jws = await _signer.SignAsync(payload, ct);

        await _projection.UpsertAsync(contractId, payload, jws, ct);
        await _events.AppendAsync(streamId, cycleId, [new ContractIssued(streamId, DateTimeOffset.UtcNow, verdict.Kind.ToString(), 
            verdict.HallucinationRisk, validUntil, version, jws)], ct);

        if (prior != null && Rank(verdict.Kind.ToString()) > Rank(prior.Verdict))
        {
            await _notifier.NotifyDowngradeAsync(asset, agentId, prior.Verdict, verdict.Kind.ToString(), 
                verdict.Findings.Select(f => f.Category).Distinct().ToList(), ct);
        }

        _metrics.Increment("warrant.certified", new Dictionary<string, string> { ["verdict"] = verdict.Kind.ToString(), ["asset"] = asset });
        _metrics.Measure("warrant.hallucination_risk", verdict.HallucinationRisk, new Dictionary<string, string> { ["asset"] = asset });
        
        return contractId;
    }

    public async Task<string> RenewAsync(Guid streamId, TaskProfile profile, CancellationToken ct = default)
    {
        var contractId = $"WRNT-{streamId:N}";
        var newUntil = DateTimeOffset.UtcNow.AddHours(profile.ContractTtlHours);
        await _projection.RenewTtlAsync(contractId, newUntil, ct);
        _metrics.Increment("warrant.renewed");
        return contractId;
    }

    public async Task<SkipDecision> ShouldSkipAsync(string contentHash, string agentId, string logicalName, CancellationToken ct = default)
    {
        var snap = await _projection.GetLatestByAssetAsync(logicalName, agentId, ct);
        
        if (snap != null && !string.IsNullOrEmpty(contentHash)
            && string.Equals(snap.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
            && snap.ValidUntil > DateTimeOffset.UtcNow.AddMinutes(-5))
        {
            _metrics.Increment("warrant.skipped_unchanged");
            return new SkipDecision(true, snap.ContentHash);
        }

        return new SkipDecision(false, snap?.ContentHash);
    }

    public async Task CertifyErrorAsync(Guid streamId, string logicalName, TaskProfile profile, string error, CancellationToken ct = default)
    {
        var contractId = $"WRNT-{streamId:N}";
        var agentId = profile.AgentId.ToString();
        var prior = await _projection.GetLatestByAssetAsync(logicalName, agentId, ct);
        var version = (prior?.Version ?? 0) + 1;
        var validUntil = DateTimeOffset.UtcNow.AddMinutes(30);
        var findings = new[] { new Finding("CertificationError", null, Truncate(error), "High") };
        var payload = new
        {
            contractId, asset = logicalName, agentId,
            taskPurpose = profile.Purpose, legalBasis = profile.LegalBasis,
            verdict = "No", hallucinationRisk = 1.0,
            findings, signalProvenance = new Dictionary<string, string[]>(),
            contentHash = "", validFrom = DateTimeOffset.UtcNow, validUntil, version
        };
        var jws = await _signer.SignAsync(payload, ct);
        await _projection.UpsertAsync(contractId, payload, jws, ct);
        _metrics.Increment("warrant.error", new Dictionary<string, string> { ["asset"] = logicalName });
    }

    private static int Rank(string v) => v switch { "Ready" => 0, "Conditional" => 1, _ => 2 };
    private static string Truncate(string s) => s.Length > 200 ? s[..200] : s;
}