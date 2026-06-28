using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Warrant.Application;
using Warrant.Certification;
using Warrant.Domain;

namespace Warrant.Orchestration;

public record CertifyInput(Guid StreamId, string LogicalName, TaskProfile TaskProfile);
public record GuardianCall(string Guardian, AssetContext Asset);
public record CertifyActivityInput(Guid StreamId, Guid CycleId, TaskProfile TaskProfile);
public record CycleInput(Guid StreamId, Guid CycleId, string LogicalName, TaskProfile TaskProfile);

public static class CertifyOrchestrator
{
    private static readonly TaskOptions Retry = TaskOptions.FromRetryPolicy(new RetryPolicy(3, TimeSpan.FromSeconds(2), backoffCoefficient: 2));

    [Function(nameof(CertifyOrchestrator))]
    public static async Task<string> Run([OrchestrationTrigger] TaskOrchestrationContext ctx)
    {
        var input = ctx.GetInput<CertifyInput>()!;
        var cycleId = ctx.NewGuid();
        var cyc = new CycleInput(input.StreamId, cycleId, input.LogicalName, input.TaskProfile);

        AssetContext asset;
        try
        {
            asset = await ctx.CallActivityAsync<AssetContext>(nameof(Activities.LoadAssetActivity), cyc, Retry);
        }
        catch (TaskFailedException ex)
        {
            await ctx.CallActivityAsync(nameof(Activities.CertifyErrorActivity), 
                new ErrorInput(input.StreamId, input.LogicalName, input.TaskProfile, ex.Message), Retry);
            await ctx.CreateTimer(ctx.CurrentUtcDateTime.AddHours(1), CancellationToken.None);
            ctx.ContinueAsNew(input);
            return "error";
        }

        var skip = await ctx.CallActivityAsync<SkipDecision>(nameof(Activities.ShouldSkipActivity),
            new SkipInput(asset.ContentHash, input.TaskProfile.AgentId.ToString(), input.LogicalName), Retry);

        string contractId;
        if (skip.Skip)
        {
            contractId = await ctx.CallActivityAsync<string>(nameof(Activities.RenewActivity),
                new CertifyActivityInput(input.StreamId, cycleId, input.TaskProfile), Retry);
        }
        else
        {
            var tasks = new[] { "OntologyMapper", "GroundingScorer", "QualitySentinel", "SignalAggregator" }
                .Select(g => ctx.CallActivityAsync<bool>(nameof(Activities.RunGuardianActivity), new GuardianCall(g, asset), Retry));
            await Task.WhenAll(tasks);

            contractId = await ctx.CallActivityAsync<string>(nameof(Activities.CertifyActivity),
                new CertifyActivityInput(input.StreamId, cycleId, input.TaskProfile), Retry);
        }

        var nextHours = Math.Max(1, Math.Min(input.TaskProfile.FreshnessToleranceHours, input.TaskProfile.ContractTtlHours));
        await ctx.CreateTimer(ctx.CurrentUtcDateTime.AddHours(nextHours), CancellationToken.None);
        ctx.ContinueAsNew(input);
        return contractId;
    }
}

public record SkipInput(
    string ContentHash, 
    string AgentId, 
    string LogicalName
);

public record ErrorInput(
    Guid StreamId, 
    string LogicalName, 
    TaskProfile TaskProfile, 
    string Error
);