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
                "No valid certificate yet - certification was triggered. Will retry shortly.", null, null, null, false));
        }

        var (verified, payloadJson) = await _verifier.VerifyAsync(c.Jws, ct);
        if (!verified)
        {
            return Json(new GateResult("blocked", false, c.Verdict, [],
                "Contract signature could not be verified - treated as untrusted.", c.HallucinationRisk, c.ValidUntil, c.Provenance, false));
        }

        var signed = ParseSigned(payloadJson!);
        if (signed == null)
        {
            return Json(new GateResult("blocked", false, c.Verdict, [],
                "Signed payload could not be read - treated as untrusted.", c.HallucinationRisk, c.ValidUntil, c.Provenance, false));
        }

        if (!string.Equals(c.Verdict, signed.Verdict, StringComparison.OrdinalIgnoreCase))
        {
            return Json(new GateResult("blocked", false, c.Verdict, [],
                $"Tamper detected: signed verdict is '{signed.Verdict}', but database shows '{c.Verdict}'.",
                signed.HallucinationRisk, c.ValidUntil, c.Provenance, false));
        }

        var safetyBlock = signed.Findings.Any(f => f.Severity == "High" && SafetyCategories.Contains(f.Category));
        if (safetyBlock)
        {
            return Json(new GateResult("blocked", false, signed.Verdict, [],
                "Blocked by a safety finding (oversharing / DLP / access).", signed.HallucinationRisk, c.ValidUntil, c.Provenance, true));
        }

        var avoid = signed.Findings
            .Where(f => f.Field != null && (f.Severity == "High" || f.Severity == "Medium"))
            .Select(f => f.Field!)
            .Distinct()
            .ToArray();

        return signed.Verdict switch
        {
            "Ready" => Json(new GateResult("full", true, "Ready", [], null, signed.HallucinationRisk, c.ValidUntil, c.Provenance, true)),
            _ => Json(new GateResult("restricted", true, signed.Verdict, avoid, BuildCaveat(signed.Verdict, signed.Findings), signed.HallucinationRisk, c.ValidUntil, c.Provenance, true))
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

    private static SignedContract? ParseSigned(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var verdict = root.GetProperty("verdict").GetString() ?? "No";
            var risk = root.TryGetProperty("hallucinationRisk", out var r) ? r.GetDouble() : 1.0;
            var findings = root.TryGetProperty("findings", out var f) && f.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize<List<Finding>>(f.GetRawText(), JsonOptions) ?? []
                : [];
            return new SignedContract(verdict, risk, findings);
        }
        catch { return null; }
    }

    private sealed record SignedContract(string Verdict, double HallucinationRisk, IReadOnlyList<Finding> Findings);

    private static string Json(GateResult r) => JsonSerializer.Serialize(r);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private record GateResult(
        string mode, bool allowed, string? verdict,
        string[] avoidFields, string? caveat,
        double? hallucinationRisk, DateTimeOffset? validUntil, string? signalProvenance,
        bool signatureVerified
    );

}