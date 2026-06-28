using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Warrant.Certification;

namespace Warrant.Infrastructure;

public sealed class DataverseContractProjection(IDataverseClientFactory f) : IContractProjection
{
    private readonly IDataverseClientFactory _factory = f;

    public async Task UpsertAsync(string contractId, object payload, string jws, CancellationToken ct = default)
    {
        var p = JsonSerializer.SerializeToElement(payload);
        var svc = await _factory.CreateAsync(ct);
        var existing = (await svc.RetrieveMultipleAsync(new QueryExpression("wrnt_contract")
        {
            TopCount = 1,
            ColumnSet = new ColumnSet(false),
            Criteria = new FilterExpression { Conditions = { new("wrnt_name", ConditionOperator.Equal, contractId) } }
        })).Entities.FirstOrDefault();

        var e = new Entity("wrnt_contract")
        {
            ["wrnt_name"] = contractId,
            ["wrnt_verdict"] = new OptionSetValue(p.GetProperty("verdict").GetString() switch { "Ready" => 1, "Conditional" => 2, _ => 3 }),
            ["wrnt_hallucinationrisk"] = (decimal)p.GetProperty("hallucinationRisk").GetDouble(),
            ["wrnt_validfrom"]  = p.GetProperty("validFrom").GetDateTimeOffset().UtcDateTime,
            ["wrnt_validuntil"] = p.GetProperty("validUntil").GetDateTimeOffset().UtcDateTime,
            ["wrnt_status"] = new OptionSetValue(1),
            ["wrnt_version"] = p.GetProperty("version").GetInt32(),
            ["wrnt_jws"] = jws,
            ["wrnt_asset_logical"] = p.GetProperty("asset").GetString(),
            ["wrnt_agent_id"] = p.GetProperty("agentId").GetString(),
            ["wrnt_signal_provenance"] = p.GetProperty("signalProvenance").GetRawText(),
            ["wrnt_findings"] = p.TryGetProperty("findings", out var f) ? f.GetRawText() : "[]",
            ["wrnt_contenthash"] = p.TryGetProperty("contentHash", out var h) ? (h.GetString() ?? "") : ""
        };

        if (existing != null)
        {
            e.Id = existing.Id; 
            await svc.UpdateAsync(e); 
        }
        else
        {
            await svc.CreateAsync(e);
        }
    }

    public async Task<ContractSnapshot?> GetLatestByAssetAsync(string asset, string agentId, CancellationToken ct = default)
    {
        var svc = await _factory.CreateAsync(ct);
        var q = new QueryExpression("wrnt_contract")
        {
            TopCount = 1,
            ColumnSet = new ColumnSet("wrnt_name", "wrnt_version", "wrnt_contenthash", "wrnt_verdict", "wrnt_validuntil"),
            Orders = { new OrderExpression("wrnt_version", OrderType.Descending) },
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new("wrnt_asset_logical", ConditionOperator.Equal, asset),
                    new("wrnt_agent_id", ConditionOperator.Equal, agentId)
                }
            }
        };
        var row = (await svc.RetrieveMultipleAsync(q)).Entities.FirstOrDefault();

        if (row == null)
        {
             return null;
        }

        var verdict = row.GetAttributeValue<OptionSetValue>("wrnt_verdict")?.Value switch { 1 => "Ready", 2 => "Conditional", _ => "No" };
       
        return new ContractSnapshot(
            row.GetAttributeValue<string>("wrnt_name"),
            row.GetAttributeValue<int?>("wrnt_version") ?? 0,
            row.GetAttributeValue<string>("wrnt_contenthash"),
            verdict,
            new DateTimeOffset(row.GetAttributeValue<DateTime>("wrnt_validuntil"), TimeSpan.Zero)
        );
    }

    public async Task RenewTtlAsync(string contractId, DateTimeOffset newValidUntil, CancellationToken ct = default)
    {
        var svc = await _factory.CreateAsync(ct);
        var q = new QueryExpression("wrnt_contract")
        {
            TopCount = 1,
            ColumnSet = new ColumnSet("wrnt_version"),
            Criteria = new FilterExpression(LogicalOperator.And) 
            { 
                Conditions = 
                { 
                    new("wrnt_name", ConditionOperator.Equal, contractId) 
                } 
            }
        };
        var row = (await svc.RetrieveMultipleAsync(q)).Entities.FirstOrDefault();
        var version = (row?.GetAttributeValue<int?>("wrnt_version") ?? 0) + 1;
        var e = new Entity("wrnt_contract", "wrnt_name", contractId)
        {
            ["wrnt_validuntil"] = newValidUntil.UtcDateTime,
            ["wrnt_version"] = version,
            ["wrnt_status"] = new OptionSetValue(1)
        };
        await svc.ExecuteAsync(new UpsertRequest { Target = e });
    }

    public async Task RevokeAsync(string asset, string agentId, CancellationToken ct = default)
    {
        var svc = await _factory.CreateAsync(ct);
        var q = new QueryExpression("wrnt_contract")
        {
            ColumnSet = new ColumnSet("wrnt_contractid"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions = 
                { 
                    new("wrnt_asset_logical", ConditionOperator.Equal, asset), 
                    new("wrnt_agent_id", ConditionOperator.Equal, agentId) 
                }
            }
        };
        foreach (var row in (await svc.RetrieveMultipleAsync(q)).Entities)
        {
            await svc.UpdateAsync(new Entity("wrnt_contract", row.Id) { ["wrnt_status"] = new OptionSetValue(2) });
        }
    }
}