namespace Warrant.Domain;

public abstract record DomainEvent(Guid StreamId, DateTimeOffset At) 
{ 
    public string Type => GetType().Name; 
}

public record CertificationCycleStarted(Guid StreamId, DateTimeOffset At, string LogicalName, Guid CycleId, string ContentHash = "") : DomainEvent(StreamId, At);
public record OntologyMapped(Guid StreamId, DateTimeOffset At, string OntologyJson, int FieldsWithoutDefinition) : DomainEvent(StreamId, At);
public record GroundingScored(Guid StreamId, DateTimeOffset At, double HallucinationRisk, IReadOnlyList<Finding> Findings) : DomainEvent(StreamId, At);
public record QualityAssessed(Guid StreamId, DateTimeOffset At, double Completeness, int Duplicates, double FreshnessHours, IReadOnlyList<Finding> Findings) : DomainEvent(StreamId, At);
public record SignalsAggregated(Guid StreamId, DateTimeOffset At, IReadOnlyList<Signal> Signals, IReadOnlyList<Finding> Findings) : DomainEvent(StreamId, At);
public record ContractIssued(Guid StreamId, DateTimeOffset At, string Verdict, double HallucinationRisk, DateTimeOffset ValidUntil, int Version, string Jws) : DomainEvent(StreamId, At);

public record GuardianFailed(Guid StreamId, DateTimeOffset At, string Guardian, string Error) : DomainEvent(StreamId, At);

public record Finding(string Category, string? Field, string Detail, string Severity);
public record Signal(string Type, string Value, string Source, string Mode, DateTimeOffset ObservedAt);