namespace Warrant.Application;

public record WaiverRule(
    string Asset, 
    string? AgentId, 
    string Category, 
    string? Field, 
    DateTimeOffset? Until
);

public interface IWaiverStore
{
    Task<IReadOnlyList<WaiverRule>> GetActiveAsync(string asset, string agentId, CancellationToken ct = default);
}