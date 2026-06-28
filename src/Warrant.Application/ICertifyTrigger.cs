namespace Warrant.Application;

public interface ICertifyTrigger 
{ 
    Task TriggerAsync(string asset, string agentId, CancellationToken ct = default); 
}