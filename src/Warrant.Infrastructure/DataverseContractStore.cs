using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Warrant.Domain;

namespace Warrant.Infrastructure;

public record ContractView(
    string Asset, 
    string Verdict, 
    double HallucinationRisk, 
    DateTimeOffset ValidUntil, 
    string Jws, 
    string Provenance, 
    IReadOnlyList<Finding> Findings
);

public interface IContractStore
{
    Task<IReadOnlyList<ContractView>> GetValidContractsAsync(CancellationToken ct = default);
    Task<ContractView?> GetValidContractAsync(string asset, string agentId, CancellationToken ct = default);
}

public sealed class DataverseContractStore(IDataverseClientFactory f) : IContractStore
{
    private readonly IDataverseClientFactory _factory = f;

    public async Task<ContractView?> GetValidContractAsync(string asset, string agentId, CancellationToken ct = default)
    {
        var svc = await _factory.CreateAsync(ct);
        var q = Base(); q.TopCount = 1;

        q.Orders.Add(new OrderExpression("wrnt_version", OrderType.Descending));
        q.Orders.Add(new OrderExpression("wrnt_validfrom", OrderType.Descending));
        q.Criteria.AddCondition("wrnt_asset_logical", ConditionOperator.Equal, asset);
        q.Criteria.AddCondition("wrnt_agent_id", ConditionOperator.Equal, agentId);

        var row = (await svc.RetrieveMultipleAsync(q)).Entities.FirstOrDefault();

        return row == null ? null : Map(row);
    }

    public async Task<IReadOnlyList<ContractView>> GetValidContractsAsync(CancellationToken ct = default)
    {
        var svc = await _factory.CreateAsync(ct);
        return (await svc.RetrieveMultipleAsync(Base())).Entities.Select(Map).ToList();
    }

    private static QueryExpression Base() => new("wrnt_contract")
    {
        ColumnSet = new ColumnSet("wrnt_asset_logical","wrnt_verdict","wrnt_hallucinationrisk",
            "wrnt_validuntil","wrnt_validfrom","wrnt_jws","wrnt_signal_provenance","wrnt_findings"),
       
        Criteria = new FilterExpression(LogicalOperator.And)
            { Conditions = { new("wrnt_status", ConditionOperator.Equal, 1), new("wrnt_validuntil", ConditionOperator.GreaterThan, DateTime.UtcNow) }}
    };

    private static ContractView Map(Entity e) => new (
        e.GetAttributeValue<string>("wrnt_asset_logical"),
        e.GetAttributeValue<OptionSetValue>("wrnt_verdict")?.Value switch { 1 => "Ready", 2 => "Conditional", _ => "No" },
        (double)(e.GetAttributeValue<decimal?>("wrnt_hallucinationrisk") ?? 1m),
        new DateTimeOffset(e.GetAttributeValue<DateTime>("wrnt_validuntil"), TimeSpan.Zero),
        e.GetAttributeValue<string>("wrnt_jws") ?? "",
        e.GetAttributeValue<string>("wrnt_signal_provenance") ?? "{}",
        ParseFindings(e.GetAttributeValue<string>("wrnt_findings"))
    );

    private static IReadOnlyList<Finding> ParseFindings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try { return JsonSerializer.Deserialize<List<Finding>>(json) ?? new(); }
        catch { return []; }
    }
}