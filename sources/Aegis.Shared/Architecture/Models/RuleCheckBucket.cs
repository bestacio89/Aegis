using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// How many times a rule of a given severity and category was actually checked inside one domain
/// (architecture layer). Violations are only the failures; these are the denominators.
/// </summary>
public sealed record RuleCheckBucket(
    string Domain,
    ArchitectureRuleSeverity Severity,
    string Category,
    int Checks);