using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Warrant.Application;
using Warrant.Domain;
using Warrant.Infrastructure;

namespace Warrant.Mcp;

[McpServerToolType]
public sealed class WarrantTools(IContractStore store, IContractVerifier verifier, ICertifyTrigger trigger)
{
    private readonly IContractStore _store = store;
    private readonly IContractVerifier _verifier = verifier;
    private readonly ICertifyTrigger _trigger = trigger;

    private static readonly HashSet<string> SafetyCategories = new(StringComparer.OrdinalIgnoreCase)
    { 
        "Oversharing", "DlpBlock", "AccessViolation", "CertificationError" 
    };

    [McpServerTool]
    public async Task<string> ListCertifiedAssets(CancellationToken ct)
        => JsonSerializer.Serialize((await _store.GetValidContractsAsync(ct)).Select(c => new { c.Asset, c.Verdict, c.ValidUntil }));

    [McpServerTool]
    public async Task<string> CheckAssetForTask(string asset, string agentId, CancellationToken ct)
    {
        var a = Normalize(asset);
        var ag = (agentId ?? "").Trim();
        var c = await _store.GetValidContractAsync(a, ag, ct);

        if (c == null)
        {
            try { await _trigger.TriggerAsync(a, ag, ct); } catch { }
            return Json(new GateResult("pending", false, null, [], 
                "No valid certificate yet - certification was triggered. Willretry shortly.", null, null, null, false));
        }

        var (verified, payloadJson) = await _verifier.VerifyAsync(c.Jws, ct);
        if (!verified)
        {
            return Json(new GateResult("blocked", false, c.Verdict, [], 
                "Contract signature could not be verified - treated as untrusted.", c.HallucinationRisk, c.ValidUntil, c.Provenance, false));
        }
        
        using var doc = JsonDocument.Parse(payloadJson!);
        var signedVerdict = doc.RootElement.GetProperty("verdict").GetString();
        if (!string.Equals(c.Verdict, signedVerdict, StringComparison.OrdinalIgnoreCase))
        {
            return Json(new GateResult("blocked", false, c.Verdict, [], 
                $"Tamper detected: Database altered, signed contract verdict is '{signedVerdict}', but database shows '{c.Verdict}'.",
                c.HallucinationRisk, c.ValidUntil, c.Provenance, false));
        }

        var safetyBlock = c.Findings.Any(f => f.Severity == "High" && SafetyCategories.Contains(f.Category));
        if (safetyBlock)
        {
            return Json(new GateResult("blocked", false, c.Verdict, [], 
                "Blocked by a safety finding (oversharing / DLP / access).", c.HallucinationRisk, c.ValidUntil, c.Provenance, true));
        }
            
        var avoid = c.Findings
            .Where(f => f.Field != null && (f.Severity == "High" || f.Severity == "Medium"))
            .Select(f => f.Field!)
            .Distinct()
            .ToArray();

        return c.Verdict switch
        {
            "Ready" => Json(new GateResult("full", true, "Ready", [], null, c.HallucinationRisk, c.ValidUntil, c.Provenance, true)),
            _ => Json(new GateResult("restricted", true, c.Verdict, avoid, BuildCaveat(c.Verdict, c.Findings), c.HallucinationRisk, c.ValidUntil, c.Provenance, true))
        };
    }

    [McpServerTool, Description("Explicitly triggers a fresh re-certification of an asset in the background. Does NOT return a verdict. Use only when the user explicitly asks to re-certify.")]
    public async Task<string> RecertifyAsset(string asset, string agentId, CancellationToken ct)
    {
        var a = Normalize(asset);
        var ag = (agentId ?? "").Trim();
        await _trigger.TriggerAsync(a, ag, ct);
        return JsonSerializer.Serialize(new { status = "recertification_triggered", asset = a, message = "Re-certification started in the background. Check the gate again in a few seconds." });
    }

    private static string BuildCaveat(string verdict, IReadOnlyList<Finding> findings)
    {
        var categories = findings.Select(f => f.Category).Distinct().Take(4);
        var why = string.Join(", ", categories);

        return verdict == "No"
            ? $"Some fields are unreliable ({why}); answer only from clean fields and flag uncertainty."
            : $"Use with care ({why}); prefer well-defined fields and note any caveats.";
    }

    private static string Normalize(string? asset)
    {
        var a = (asset ?? "").Trim().ToLowerInvariant();
        if (a.EndsWith("ies"))
        {
            a = a[..^3] + "y";
        }
        else if (a.EndsWith("s") && !a.EndsWith("ss"))
        {
            a = a[..^1];
        }
        return a.Replace(" ", "");
    }
    
    private static string Json(GateResult r) => JsonSerializer.Serialize(r);

    private record GateResult(
        string mode, bool allowed, string? verdict,
        string[] avoidFields, string? caveat,
        double? hallucinationRisk, DateTimeOffset? validUntil, string? signalProvenance,
        bool signatureVerified
    );

}