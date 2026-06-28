using Microsoft.Extensions.Logging;
using Warrant.Application;
using Warrant.Domain;

namespace Warrant.Guardians;

public sealed class SignalAggregator(IEnumerable<IExternalSignalSource> sources, IDataverseSecurityReader rls, ILogger<SignalAggregator> log) : IGuardian
{
    public string Name => "SignalAggregator";
    private readonly IReadOnlyList<IExternalSignalSource> _sources = sources.ToList();
    private readonly IDataverseSecurityReader _rls = rls;
    private readonly ILogger<SignalAggregator> _log = log;

    public async Task<IReadOnlyList<DomainEvent>> InspectAsync(AssetContext ctx, CancellationToken ct = default)
    {
        var signals = new List<Signal>();
        var findings = new List<Finding>();

        var rls = await _rls.GetAccessPostureAsync(ctx.LogicalName, ct);
        signals.Add(new("DataverseRLS", rls.IsOverShared ? "OverShared" : "Scoped", "Dataverse", "Native", DateTimeOffset.UtcNow));

        var hasSensitive = ctx.Fields.Any(f => f.IsPii);
        if (rls.IsOverShared && hasSensitive)
        {
            findings.Add(new("Oversharing", null, "Org-wide reach on a table containing sensitive fields", "High"));
        }

        foreach (var src in _sources)
        {
            try
            {
                var s = await src.ReadAsync(ctx.LogicalName, ct);
                signals.AddRange(s);
                if (s.Any(x => x.Type == "PurviewDLP" && x.Value.Contains("Block", StringComparison.OrdinalIgnoreCase)))
                {
                    findings.Add(new("DlpBlock", null, $"{src.Capability}/{src.Mode}: DLP blocks grounding for this asset", "High"));
                }
                _log.LogInformation("Source {Cap}/{Mode} -> {N} signal(s)", src.Capability, src.Mode, s.Count);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Signal source {Cap} failed", src.Capability);
                findings.Add(new("GuardianUnavailable", src.Capability, $"Signal source failed: {ex.Message}", "Medium"));
            }
        }

        return [new SignalsAggregated(ctx.StreamId, DateTimeOffset.UtcNow, signals, findings)];
    }
}