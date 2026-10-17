namespace Aegis.Shared.Architecture.Models.Rules;

/// <summary>
/// Describes the outcome of evaluating a rule against evidence.
/// A status is independent of whether a finding was generated.
/// </summary>
public enum RuleEvaluationStatus
{
    Passed,
    Violated,
    NotApplicable,
    NotEvaluated,
    InsufficientEvidence,
    Error
}