using System.Text.Json;
using Warrant.Application;
using Warrant.Domain;

namespace Warrant.Guardians;

public sealed class OntologyMapper : IGuardian
{
    public string Name => "OntologyMapper";

    public Task<IReadOnlyList<DomainEvent>> InspectAsync(AssetContext ctx, CancellationToken ct = default)
    {
        var noDef = ctx.Fields.Count(f => string.IsNullOrWhiteSpace(f.Description));
        var priorityNoDef = ctx.Fields.Count(f => f.IsPriority && string.IsNullOrWhiteSpace(f.Description));

        var ontology = JsonSerializer.Serialize(new
        {
            entity = ctx.LogicalName,
            priorityFieldsWithoutDefinition = priorityNoDef,
            fields = ctx.Fields.Select(f => new { f.LogicalName, f.Type, f.IsPriority, hasDefinition = !string.IsNullOrWhiteSpace(f.Description) })
        });

        IReadOnlyList<DomainEvent> ev = [new OntologyMapped(ctx.StreamId, DateTimeOffset.UtcNow, ontology, noDef)];
        return Task.FromResult(ev);
    }
}