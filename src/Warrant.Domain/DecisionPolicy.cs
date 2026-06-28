namespace Warrant.Domain;

public sealed record DecisionPolicy(
    double NoRiskThreshold = 0.66,
    double MinCompletenessForReady = 0.80,
    double MinCompletenessForConditional = 0.50,
    int MaxDuplicates = 0
);