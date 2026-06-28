using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Warrant.Application;

namespace Warrant.Infrastructure;

public sealed class DataverseAssetLoader(IDataverseClientFactory f, EntityMetadataCache metaCache) : IAssetLoader
{
    private const int SampleSize = 8;
    
    private static readonly HashSet<string> SystemFields = new(StringComparer.OrdinalIgnoreCase)
    { "createdon","modifiedon","createdby","modifiedby","createdonbehalfby","modifiedonbehalfby",
      "ownerid","owningbusinessunit","owninguser","owningteam","versionnumber","statecode","statuscode",
      "importsequencenumber","timezoneruleversionnumber","utcconversiontimezonecode","overriddencreatedon" };

    private readonly IDataverseClientFactory _factory = f;
    private readonly EntityMetadataCache _metaCache = metaCache;

    public async Task<AssetContext> LoadAsync(Guid streamId, Guid cycleId, string logicalName, string[] priorityFields, CancellationToken ct = default)
    {
        var svc = await _factory.CreateAsync(ct);
        var meta = await _metaCache.GetAsync(_factory, logicalName, EntityFilters.Attributes, ct);
        var priority = new HashSet<string>(priorityFields ?? [], StringComparer.OrdinalIgnoreCase);

        var candidates = meta.Attributes
            .Where(a => a.IsValidForRead == true 
                && a.AttributeType != null
                && a.IsLogical != true
                && string.IsNullOrEmpty(a.AttributeOf)
                && a.AttributeType != AttributeTypeCode.Uniqueidentifier
                && !SystemFields.Contains(a.LogicalName))
            .ToList();

        var fields = candidates
            .OrderByDescending(a => priority.Contains(a.LogicalName))
            .ThenByDescending(a => a.IsCustomAttribute == true)
            .Take(20)
            .Select(a => new FieldMeta(
                a.LogicalName,
                a.DisplayName?.UserLocalizedLabel?.Label ?? a.LogicalName,
                a.AttributeType!.ToString()!,
                a.Description?.UserLocalizedLabel?.Label,
                a.LogicalName.Contains("email") || a.LogicalName.Contains("phone"),
                priority.Contains(a.LogicalName)))
            .ToList();

        var cols = new ColumnSet(fields.Select(f => f.LogicalName).ToArray());
        var half = Math.Max(1, SampleSize / 2);
        var seen = new HashSet<Guid>();

        var newest = (await svc.RetrieveMultipleAsync(new QueryExpression(logicalName)
        { 
            TopCount = half, ColumnSet = cols,
            Orders = { new OrderExpression("modifiedon", OrderType.Descending) } 
        })).Entities;

        var oldest = (await svc.RetrieveMultipleAsync(new QueryExpression(logicalName)
        {   TopCount = half, ColumnSet = cols,
            Orders = { new OrderExpression("modifiedon", OrderType.Ascending) } 
        })).Entities;
        
        var rows = newest.Concat(oldest)
            .Where(e => seen.Add(e.Id))
            .Select(e => (IReadOnlyDictionary<string, object?>)e.Attributes.ToDictionary(kv => kv.Key, kv => Flatten(kv.Value)))
            .ToList();

        var last = newest.FirstOrDefault()?.GetAttributeValue<DateTime?>("modifiedon") ?? DateTime.UtcNow;
        var contentHash = ComputeContentHash(fields, rows);

        TableStats? stats = null;
        var dupKey = GuardianHeuristics.PickDupKey(fields);
        var primaryId = string.IsNullOrEmpty(meta.PrimaryIdAttribute) ? logicalName + "id" : meta.PrimaryIdAttribute;
        try
        {
            var fx = BuildStatsFetch(logicalName, primaryId, fields, dupKey);
            var agg = (await svc.RetrieveMultipleAsync(new FetchExpression(fx))).Entities.FirstOrDefault();
            if (agg != null)
            {
                stats = BuildStats(agg, fields, dupKey);
            }
        }
        catch 
        { 
            stats = null; 
        }

       return new AssetContext(streamId, cycleId, logicalName, fields, rows, new DateTimeOffset(last, TimeSpan.Zero), contentHash, stats);
    }

    private static string ComputeContentHash(IReadOnlyList<FieldMeta> fields, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        
        foreach (var f in fields.OrderBy(f => f.LogicalName, StringComparer.Ordinal))
        {
            sb.Append(f.LogicalName).Append('|').Append(f.Type).Append('|').Append(f.Description ?? "").Append(';');
        }
        
        sb.Append("##");

        foreach (var r in rows)
            foreach (var kv in r.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append('=').Append(kv.Value?.ToString() ?? "").Append(',');

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static string BuildStatsFetch(string logicalName, string primaryId, IReadOnlyList<FieldMeta> fields, string? key)
    {
        var sb = new StringBuilder();
        sb.Append("<fetch aggregate='true'>");
        sb.Append($"<entity name='{logicalName}'>");
        sb.Append($"<attribute name='{primaryId}' alias='total' aggregate='count' />");
        
        for (int i = 0; i < fields.Count; i++)
        {
            sb.Append($"<attribute name='{fields[i].LogicalName}' alias='f{i}' aggregate='countcolumn' />");
        }
        if (key != null)
        {
            sb.Append($"<attribute name='{key}' alias='knn' aggregate='countcolumn' />");
            sb.Append($"<attribute name='{key}' alias='kd' aggregate='countcolumn' distinct='true' />");
        }
        
        sb.Append("</entity></fetch>");
        return sb.ToString();
    }

    private static TableStats BuildStats(Entity agg, IReadOnlyList<FieldMeta> fields, string? key)
    {
        var total = AggInt(agg, "total");
        var filled = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < fields.Count; i++)
        {
            filled[fields[i].LogicalName] = AggInt(agg, $"f{i}");
        }
        var dup = key == null ? 0 : Math.Max(0, AggInt(agg, "knn") - AggInt(agg, "kd"));

        return new TableStats(true, total, filled, key, dup);
    }

    private static int AggInt(Entity e, string alias)
        => e.TryGetAttributeValue<AliasedValue>(alias, out var av) && av?.Value != null ? Convert.ToInt32(av.Value) : 0;

    private static object? Flatten(object? v) 
        => v switch { EntityReference r => r.Name ?? r.Id.ToString(), OptionSetValue o => o.Value, Money m => m.Value, _ => v };
}