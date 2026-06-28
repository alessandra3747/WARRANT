using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Warrant.Application;

namespace Warrant.Infrastructure;

public sealed class HttpCertifyTrigger(HttpClient http, IOptions<WarrantOptions> opt, ILogger<HttpCertifyTrigger> log) : ICertifyTrigger
{
    private readonly HttpClient _http = http;
    private readonly OnDemandOptions _o = opt.Value.OnDemand;
    private readonly ILogger<HttpCertifyTrigger> _log = log;

    public async Task TriggerAsync(string asset, string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_o.CertifyEndpoint)) 
        { 
            _log.LogInformation("On-demand certify endpoint not configured; skipping {Asset}", asset); 
            return; 
        }

        var streamId = DeterministicGuid($"{asset}|{agentId}");
        var agentGuid = Guid.TryParse(agentId, out var g) ? g : DeterministicGuid(agentId);
        var body = new
        {
            streamId,
            logicalName = asset,
            taskProfile = new
            {
                agentId = agentGuid,
                requiredEntities = new[] { asset },
                freshnessToleranceHours = 24.0,
                ambiguityTolerance = 0.3,
                purpose = "on-demand",
                legalBasis = "legitimate interest",
                requiredFields = (string[]?)null,
                contractTtlHours = 12.0
            }
        };

        var url = _o.CertifyEndpoint + (string.IsNullOrEmpty(_o.FunctionKey) ? "" : $"?code={_o.FunctionKey}");
        try 
        { 
            await _http.PostAsJsonAsync(url, body, ct); 
        }
        catch (Exception ex) 
        {
             _log.LogError(ex, "On-demand certify failed for {Asset}", asset); 
        }
    }

    private static Guid DeterministicGuid(string s)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(s));
        return new Guid(bytes);
    }
}