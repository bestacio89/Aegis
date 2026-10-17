using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Complete result of rule interpretation.
/// Findings and evaluation outcomes are separate collections.
/// </summary>
public sealed record RuleEvaluationOutcome(
    IReadOnlyList<ArchitectureRuleresult> Results,
    RuleEvaluationStats Stats,
    IReadOnlyList<RuleEvaluationResult> Evaluations);