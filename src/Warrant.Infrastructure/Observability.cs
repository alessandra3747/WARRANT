using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Warrant.Application;

namespace Warrant.Infrastructure;

public sealed class TeamsNotifier(HttpClient http, IOptions<WarrantOptions> opt, ILogger<TeamsNotifier> log) : INotifier
{
    private readonly HttpClient _http = http;
    private readonly string? _webhook = opt.Value.Notifications.TeamsWebhookUrl;
    private readonly ILogger<TeamsNotifier> _log = log;

    public async Task NotifyDowngradeAsync(string asset, string agentId, string fromVerdict, string toVerdict, IReadOnlyList<string> reasons, CancellationToken ct = default)
    {
        _log.LogWarning("WARRANT downgrade {Asset} {From}->{To} ({Reasons})", asset, fromVerdict, toVerdict, string.Join(",", reasons));
        if (string.IsNullOrWhiteSpace(_webhook))
        {
            return;
        }
        try
        {
            var card = new { text = $"WARRANT: **{asset}** changed {fromVerdict} → {toVerdict}. Reasons: {string.Join(", ", reasons)}. Agent: {agentId}" };
            await _http.PostAsJsonAsync(_webhook, card, ct);
        }
        catch (Exception ex) 
        { 
            _log.LogError(ex, "Teams notify failed"); 
        }
    }
}

public sealed class LoggerMetrics(ILogger<LoggerMetrics> log) : IMetrics
{
    private readonly ILogger<LoggerMetrics> _log = log;

    public void Increment(string name, IReadOnlyDictionary<string, string>? tags = null)
        => _log.LogInformation("metric {Name} +1 {Tags}", name, Fmt(tags));

    public void Measure(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
        => _log.LogInformation("metric {Name} = {Value} {Tags}", name, value, Fmt(tags));
        
    private static string Fmt(IReadOnlyDictionary<string, string>? t)
        => t == null ? "" : string.Join(",", t.Select(kv => $"{kv.Key}={kv.Value}"));
}