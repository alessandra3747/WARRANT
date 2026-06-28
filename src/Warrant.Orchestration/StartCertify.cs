using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;

namespace Warrant.Orchestration;

public static class StartCertify
{
    [Function(nameof(StartCertify))]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "certify")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var body = await req.ReadFromJsonAsync<CertifyInput>();

        var instanceId = body!.StreamId.ToString("N");
        var existing = await client.GetInstanceAsync(instanceId);

        if (existing != null && existing.RuntimeStatus is OrchestrationRuntimeStatus.Running or OrchestrationRuntimeStatus.Pending or OrchestrationRuntimeStatus.Suspended)
        {
            await client.TerminateInstanceAsync(instanceId, "recertify");
            await client.WaitForInstanceCompletionAsync(instanceId, getInputsAndOutputs: false, CancellationToken.None);
        }

        await client.ScheduleNewOrchestrationInstanceAsync(nameof(CertifyOrchestrator), body!, new StartOrchestrationOptions(InstanceId: instanceId));

        var resp = req.CreateResponse(HttpStatusCode.Accepted);
        await resp.WriteStringAsync(instanceId);
        
        return resp;
    }
}