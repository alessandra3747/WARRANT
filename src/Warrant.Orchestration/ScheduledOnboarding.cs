using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk.Query;
using Warrant.Domain;
using Warrant.Infrastructure;

namespace Warrant.Orchestration;

public class ScheduledOnboarding(IDataverseClientFactory factory, ILogger<ScheduledOnboarding> log)
{
    private readonly IDataverseClientFactory _factory = factory;
    private readonly ILogger<ScheduledOnboarding> _log = log;

    [Function(nameof(ScheduledOnboarding))]
    public async Task Run([TimerTrigger("0 0 */6 * * *")] TimerInfo timer, [DurableClient] DurableTaskClient client)
    {
        var svc = await _factory.CreateAsync();
        var q = new QueryExpression("wrnt_dataasset")
        {
            ColumnSet = new ColumnSet("wrnt_logicalname"),
            Criteria = new FilterExpression()
        };
        var rows = (await svc.RetrieveMultipleAsync(q)).Entities;

        foreach (var row in rows)
        {
            var logical = row.GetAttributeValue<string>("wrnt_logicalname");
            if (string.IsNullOrWhiteSpace(logical))
            {
                continue;
            }

            var agentId = Guid.Empty;
            var streamId = Deterministic($"{logical}|{agentId}");
            var profile = new TaskProfile(agentId, new[] { logical }, 24, 0.3, "scheduled-onboarding", "legitimate interest", null, 12);
            var input = new CertifyInput(streamId, logical, profile);
            var instanceId = streamId.ToString("N");
            var existing = await client.GetInstanceAsync(instanceId);

            if (existing == null || existing.RuntimeStatus is OrchestrationRuntimeStatus.Completed or OrchestrationRuntimeStatus.Failed or OrchestrationRuntimeStatus.Terminated)
            {
                await client.ScheduleNewOrchestrationInstanceAsync(nameof(CertifyOrchestrator), input, new StartOrchestrationOptions(InstanceId: instanceId));
                _log.LogInformation("Onboarding scheduled for {Asset}", logical);
            }
        }
    }

    private static Guid Deterministic(string s)
        => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(s)));
}