using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Warrant.Certification;

namespace Warrant.Orchestration;

public record RevokeRequest(string Asset, string AgentId);

public class RevokeCertify(IContractProjection projection)
{
    private readonly IContractProjection _projection = projection;

    [Function(nameof(RevokeCertify))]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = "revoke")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<RevokeRequest>();
        if (body == null) 
        { 
            var bad = req.CreateResponse(HttpStatusCode.BadRequest); 
            return bad; 
        }

        await _projection.RevokeAsync(body.Asset, body.AgentId);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteStringAsync("revoked");
        
        return resp;
    }
}