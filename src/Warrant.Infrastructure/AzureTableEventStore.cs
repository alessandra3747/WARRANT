using System.Text.Json;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Warrant.Application;
using Warrant.Domain;

namespace Warrant.Infrastructure;

public sealed class AzureTableEventStore : IEventStore
{
    private readonly TableClient _table;
    private readonly ILogger<AzureTableEventStore> _log;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public AzureTableEventStore(IOptions<WarrantOptions> opt, ILogger<AzureTableEventStore> log)
    {
        _log = log;
        var s = opt.Value.Storage;
        _table = string.IsNullOrEmpty(s.ConnectionString)
            ? new TableClient(new Uri(s.TableEndpoint), "WarrantEvents", new DefaultAzureCredential())
            : new TableClient(s.ConnectionString, "WarrantEvents");
        _table.CreateIfNotExists();
    }

    private static string Pk(Guid streamId, Guid cycleId) => $"{streamId:N}:{cycleId:N}";

    public async Task AppendAsync(Guid streamId, Guid cycleId, IEnumerable<DomainEvent> events, CancellationToken ct = default)
    {
        var pk = Pk(streamId, cycleId);
        foreach (var e in events)
        {
            var rowKey = $"{e.At.UtcTicks:D19}-{Guid.NewGuid():N}";
            await _table.AddEntityAsync(new TableEntity(pk, rowKey)
                { ["Type"] = e.Type, ["Payload"] = JsonSerializer.Serialize(e, e.GetType(), Json), ["At"] = e.At }, ct);
        }
        _log.LogInformation("Appended {Count} event(s) to {Pk}", events.Count(), pk);
    }

    public async Task<IReadOnlyList<DomainEvent>> ReadCycleAsync(Guid streamId, Guid cycleId, CancellationToken ct = default)
    {
        var pk = Pk(streamId, cycleId);
        var list = new List<(string rk, DomainEvent ev)>();

        await foreach (var row in _table.QueryAsync<TableEntity>(x => x.PartitionKey == pk, cancellationToken: ct))
        {
            var type = typeof(DomainEvent).Assembly.GetType($"Warrant.Domain.{row.GetString("Type")}")!;
            list.Add((row.RowKey, (DomainEvent)JsonSerializer.Deserialize(row.GetString("Payload"), type, Json)!));
        }
        
        return list.OrderBy(x => x.rk, StringComparer.Ordinal).Select(x => x.ev).ToList();
    }
}