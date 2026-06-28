using Microsoft.Xrm.Sdk.Query;
using Warrant.Application;

namespace Warrant.Infrastructure;

public sealed class DataverseWaiverStore(IDataverseClientFactory f) : IWaiverStore
{
    private readonly IDataverseClientFactory _factory = f;

    public async Task<IReadOnlyList<WaiverRule>> GetActiveAsync(string asset, string agentId, CancellationToken ct = default)
    {
        var svc = await _factory.CreateAsync(ct);
        var q = new QueryExpression("wrnt_waiver")
        {
            ColumnSet = new ColumnSet("wrnt_asset_logical", "wrnt_agent_id", "wrnt_category", "wrnt_field", "wrnt_until", "wrnt_active"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new("wrnt_asset_logical", ConditionOperator.Equal, asset),
                    new("wrnt_active", ConditionOperator.Equal, true)
                }
            }
        };

        var rows = (await svc.RetrieveMultipleAsync(q)).Entities;
        var now = DateTimeOffset.UtcNow;

        return rows
            .Select(e => new WaiverRule(
                e.GetAttributeValue<string>("wrnt_asset_logical"),
                e.GetAttributeValue<string>("wrnt_agent_id"),
                e.GetAttributeValue<string>("wrnt_category") ?? "",
                e.GetAttributeValue<string>("wrnt_field"),
                e.Contains("wrnt_until") ? new DateTimeOffset(e.GetAttributeValue<DateTime>("wrnt_until"), TimeSpan.Zero) : null))
            .Where(w => string.IsNullOrEmpty(w.AgentId) || string.Equals(w.AgentId, agentId, StringComparison.OrdinalIgnoreCase))
            .Where(w => w.Until is null || w.Until > now)
            .ToList();
    }
}