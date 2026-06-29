using Warrant.Domain;
namespace Warrant.Application;

public record AssetContext(
    Guid StreamId, Guid CycleId, string LogicalName,
    IReadOnlyList<FieldMeta> Fields,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> SampleRows,
    DateTimeOffset LastModified, string ContentHash,
    TableStats? Stats = null
);

public sealed record TableStats(
    bool Exact,
    int RowCount,
    IReadOnlyDictionary<string, int> FilledByField,
    string? DupKeyField,
    int DupRedundantRows
);

public record FieldMeta(
    string LogicalName, 
    string DisplayName, 
    string Type, 
    string? Description, 
    bool IsPii, 
    bool IsPriority
);

public interface IGuardian 
{ 
    string Name { get; } 
    Task<IReadOnlyList<DomainEvent>> InspectAsync(AssetContext ctx, CancellationToken ct = default); 
}

public interface IAssetLoader 
{ 
    Task<AssetContext> LoadAsync(Guid streamId, Guid cycleId, string logicalName, string[] priorityFields, CancellationToken ct = default); 
}

public static class GuardianHeuristics
{
    private static readonly string[] KeyHints = ["email", "emailaddress", "name", "accountnumber", "phone", "telephone", "number", "id"];

    private static readonly string[] ChoiceTypes = ["Picklist", "OptionSet", "Lookup", "Customer", "State", "Status", "Boolean", "MultiSelectPicklist", "EntityName"];

    public static string? PickDupKey(IReadOnlyList<FieldMeta> fields)
    {
        bool IsChoice(FieldMeta f) => ChoiceTypes.Any(t => f.Type.Equals(t, StringComparison.OrdinalIgnoreCase));

        return fields.FirstOrDefault(f =>
                    KeyHints.Any(h => f.LogicalName.Contains(h, StringComparison.OrdinalIgnoreCase)) 
                    && !IsChoice(f))?.LogicalName ?? fields.FirstOrDefault(f => !IsChoice(f))?.LogicalName;
    }
}