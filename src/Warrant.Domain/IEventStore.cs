namespace Warrant.Domain;

public interface IEventStore
{
    Task AppendAsync(Guid streamId, Guid cycleId, IEnumerable<DomainEvent> events, CancellationToken ct = default);
    Task<IReadOnlyList<DomainEvent>> ReadCycleAsync(Guid streamId, Guid cycleId, CancellationToken ct = default);
}