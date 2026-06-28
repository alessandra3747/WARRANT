namespace Warrant.Domain;

public sealed class CertificationAggregate
{
    public Guid Id { get; private set; }
    public string? LogicalName { get; private set; }
    public string ContentHash { get; private set; } = "";
    public double? HallucinationRisk { get; private set; }
    public double? Completeness { get; private set; }
    public double? FreshnessHours { get; private set; }
    public int Duplicates { get; private set; }
    public List<Finding> Findings { get; } = new();
    public List<Signal> Signals { get; } = new();
    public int IssuedCount { get; private set; }

    public static CertificationAggregate Rehydrate(IEnumerable<DomainEvent> events)
    { 
        var a = new CertificationAggregate(); 
        foreach (var e in events)
        {
            a.Apply(e); 
        }
        return a; 
    }

    public void Apply(DomainEvent e)
    {
        switch (e)
        {
            case CertificationCycleStarted cs:
                Id = cs.StreamId; LogicalName = cs.LogicalName; ContentHash = cs.ContentHash;
                Findings.Clear(); Signals.Clear();
                HallucinationRisk = null; Completeness = null; FreshnessHours = null; Duplicates = 0;
                break;
            case OntologyMapped om when om.FieldsWithoutDefinition > 0:
                Findings.Add(new("NoDefinition", null, $"{om.FieldsWithoutDefinition} field(s) without a definition", "Medium")); break;
            case GroundingScored gs: 
                HallucinationRisk = gs.HallucinationRisk; Findings.AddRange(gs.Findings); break;
            case QualityAssessed qa:
                Completeness = qa.Completeness; FreshnessHours = qa.FreshnessHours; Duplicates = qa.Duplicates;
                Findings.AddRange(qa.Findings); break;
            case SignalsAggregated sa: 
                Signals.AddRange(sa.Signals); Findings.AddRange(sa.Findings); break;
            case GuardianFailed gf:
                Findings.Add(new("GuardianUnavailable", gf.Guardian, $"{gf.Guardian} failed: {gf.Error}", "Medium")); break;
            case ContractIssued:
                IssuedCount++; break;
        }
    }

    public void RemoveWaived(Func<Finding, bool> isWaived) => Findings.RemoveAll(f => isWaived(f));

    public Verdict Decide(TaskProfile p, DecisionPolicy policy)
    {
        var risk = HallucinationRisk ?? 1.0;
        var completeness = Completeness ?? 1.0;
        var stale = (FreshnessHours ?? double.MaxValue) > p.FreshnessToleranceHours;
        var overshared = Signals.Any(s => s is { Type: "DataverseRLS", Value: "OverShared" });
        var blocking = Findings.Any(f => f.Severity == "High");

        if (blocking || risk > policy.NoRiskThreshold || completeness < policy.MinCompletenessForConditional)
        {
            return new(VerdictKind.No, risk, Findings);
        }
        if (stale || overshared || risk > p.AmbiguityTolerance || completeness < policy.MinCompletenessForReady || Duplicates > policy.MaxDuplicates)
        {
            return new(VerdictKind.Conditional, risk, Findings);
        }
        return new(VerdictKind.Ready, risk, Findings);
    }
}

public enum VerdictKind 
{ 
    Ready, Conditional, No 
}

public record Verdict(
    VerdictKind Kind, 
    double HallucinationRisk, 
    IReadOnlyList<Finding> Findings
);

public record TaskProfile(
    Guid AgentId, 
    string[] RequiredEntities,
    double FreshnessToleranceHours, 
    double AmbiguityTolerance, 
    string Purpose, 
    string LegalBasis,
    string[]? RequiredFields = null, 
    double ContractTtlHours = 12
);