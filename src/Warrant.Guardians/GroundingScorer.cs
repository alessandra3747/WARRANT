using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Warrant.Application;
using Warrant.Domain;

namespace Warrant.Guardians;

public sealed class GroundingScorer(Kernel kernel, ILogger<GroundingScorer> log) : IGuardian
{
    public string Name => "GroundingScorer";
    private readonly Kernel _kernel = kernel;
    private readonly ILogger<GroundingScorer> _log = log;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<DomainEvent>> InspectAsync(AssetContext ctx, CancellationToken ct = default)
    {
        var chat = _kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory();
        history.AddSystemMessage(
            "You audit a Dataverse asset for AI grounding readiness. Score ONLY hallucination risk from the DATA. " +
            "STRICT RULES: " +
            "1. Self-Explanatory Names: If a field lacks a description but its logical name is clear and standard or contains words defining its obvious purpose (e.g., 'producttype', 'price', 'name', 'category', 'wrnt_country'), do NOT flag it as 'NoDefinition'. " +
            "2. OptionSets/Picklists: Dataverse inherently maps codes to localized names. Do NOT flag categorical fields for missing dictionaries. " +
            "3. Categorical Duplicates: Do NOT flag repeating values in categorical fields (like status, category, type) as 'ExactDuplicate'. Multiple records sharing a category is expected behavior. " +
            "Priority fields weigh more. Return JSON ONLY per the schema."
        );
        history.AddUserMessage(BuildPrompt(ctx));

        var settings = new OpenAIPromptExecutionSettings { Temperature = 0, ResponseFormat = "json_object" };
        try
        {
            var resp = await chat.GetChatMessageContentAsync(history, settings, _kernel, ct);
            var parsed = ParseOrFallback(resp.Content);
            var findings = FindingSanitizer.Clean(parsed.Findings.Select(f => new Finding(f.Category, f.Field, f.Detail, f.Severity)));
            return [new GroundingScored(ctx.StreamId, DateTimeOffset.UtcNow, parsed.HallucinationRisk, findings)];
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GroundingScorer failed for {Asset} - conservative fallback", ctx.LogicalName);
            return [ new GroundingScored(ctx.StreamId, DateTimeOffset.UtcNow, 0.75, [new Finding("Ambiguous", null, "Scoring failed - treated cautiously", "Medium")]) ];
        }
    }

    private static string BuildPrompt(AssetContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ASSET: {ctx.LogicalName}");
        sb.AppendLine("FIELDS (logical name | type | priority | description):");

        foreach (var f in ctx.Fields)
        {
            sb.AppendLine($"- {f.LogicalName} | {f.Type} | {(f.IsPriority ? "PRIORITY" : "-")} | {(string.IsNullOrWhiteSpace(f.Description) ? "(NO DESCRIPTION)" : f.Description)}");
        }
        
        sb.AppendLine("\nSAMPLE (values redacted where sensitive):");
        
        foreach (var r in ctx.SampleRows.Take(8))
        {
            sb.AppendLine("- " + string.Join(", ", r.Select(kv => $"{kv.Key}={Redact(ctx, kv.Key, kv.Value)}")));
        }
        
        sb.AppendLine("""

            Return JSON: {"hallucinationRisk":<0..1>,"findings":[{"category":"NoDefinition|Conflict|Ambiguous|Incomplete","field":"<name>","detail":"<short, NO raw values>","severity":"Low|Medium|High"}]}
            """);
            
        return sb.ToString();
    }

    private static string Redact(AssetContext ctx, string field, object? value)
    {
        var meta = ctx.Fields.FirstOrDefault(f => f.LogicalName == field);
        if (meta?.IsPii == true)
        {
            return "[redacted]";
        }
        var s = value?.ToString() ?? "";
        return s.Length > 60 ? s[..60] + "…" : s;
    }

    private static ScoreResult ParseOrFallback(string? c)
    {
        if (string.IsNullOrWhiteSpace(c))
        {
            return new(1.0, []);
        }

        int s = c.IndexOf('{'), e = c.LastIndexOf('}');
        var j = s >= 0 && e > s ? c[s..(e + 1)] : "{}";
        
        try 
        { 
            return JsonSerializer.Deserialize<ScoreResult>(j, Json) ?? new(0.75, Array.Empty<RawFinding>()); 
        }
        catch 
        { 
            return new(0.75, []); 
        }
    }

    private record ScoreResult(double HallucinationRisk, RawFinding[] Findings);

    private record RawFinding(string Category, string? Field, string Detail, string Severity);
    
}