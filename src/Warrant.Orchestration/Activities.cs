using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Warrant.Application;
using Warrant.Certification;
using Warrant.Domain;

namespace Warrant.Orchestration;

public class Activities(IAssetLoader loader, IEnumerable<IGuardian> guardians, ICertifier certifier, IEventStore events, ILogger<Activities> log)
{
    private readonly IAssetLoader _loader = loader;
    private readonly IReadOnlyDictionary<string, IGuardian> _guardians = guardians.ToDictionary(g => g.Name);
    private readonly ICertifier _certifier = certifier;
    private readonly IEventStore _events = events;
    private readonly ILogger<Activities> _log = log;

    [Function(nameof(LoadAssetActivity))]
    public async Task<AssetContext> LoadAssetActivity([ActivityTrigger] CycleInput input)
    {
        var priority = input.TaskProfile.RequiredFields ?? [];
        var asset = await _loader.LoadAsync(input.StreamId, input.CycleId, input.LogicalName, priority);
        
        await _events.AppendAsync(input.StreamId, input.CycleId, new[]
            { new CertificationCycleStarted(input.StreamId, DateTimeOffset.UtcNow, input.LogicalName, input.CycleId, asset.ContentHash) });
        
        return asset;
    }

    [Function(nameof(RunGuardianActivity))]
    public async Task<bool> RunGuardianActivity([ActivityTrigger] GuardianCall call)
    {
        try
        {
            var produced = await _guardians[call.Guardian].InspectAsync(call.Asset);
            await _events.AppendAsync(call.Asset.StreamId, call.Asset.CycleId, produced);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Guardian {Guardian} failed for {Asset}", call.Guardian, call.Asset.LogicalName);
            await _events.AppendAsync(call.Asset.StreamId, call.Asset.CycleId,
                new[] { new GuardianFailed(call.Asset.StreamId, DateTimeOffset.UtcNow, call.Guardian, ex.Message) });
        }
        return true;
    }

    [Function(nameof(CertifyActivity))]
    public Task<string> CertifyActivity([ActivityTrigger] CertifyActivityInput input)
        => _certifier.CertifyAsync(input.StreamId, input.CycleId, input.TaskProfile);

    [Function(nameof(RenewActivity))]
    public Task<string> RenewActivity([ActivityTrigger] CertifyActivityInput input)
        => _certifier.RenewAsync(input.StreamId, input.TaskProfile);

    [Function(nameof(ShouldSkipActivity))]
    public Task<SkipDecision> ShouldSkipActivity([ActivityTrigger] SkipInput input)
        => _certifier.ShouldSkipAsync(input.ContentHash, input.AgentId, input.LogicalName);

    [Function(nameof(CertifyErrorActivity))]
    public Task CertifyErrorActivity([ActivityTrigger] ErrorInput input)
        => _certifier.CertifyErrorAsync(input.StreamId, input.LogicalName, input.TaskProfile, input.Error);
}