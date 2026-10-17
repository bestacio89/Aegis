using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models;

/// <summary>Violations found by the rule engine plus the statistics of what it checked.</summary>
public sealed record RuleEvaluationOutcome(
    IReadOnlyList<ArchitectureRuleresult> Results,
    RuleEvaluationStats Stats);