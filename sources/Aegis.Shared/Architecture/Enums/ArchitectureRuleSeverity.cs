namespace Aegis.Shared.Architecture.Enums;

/// <summary>
/// Represents the severity and architectural impact level of a rule violation.
/// </summary>
public enum ArchitectureRuleSeverity
{
    /// <summary>
    /// Informational only — used for best practices or soft suggestions.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Low impact — minor style or convention issue, does not affect maintainability.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Medium impact — moderate design issue that may affect readability or extensibility.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// High impact — major architectural or design flaw with long-term maintainability risk.
    /// </summary>
    High = 3,

    /// <summary>
    /// Critical impact — severe violation that compromises architectural integrity.
    /// </summary>
    Critical = 4,

    /// <summary>
    /// Blocker — catastrophic violation that invalidates compliance (e.g., circular dependency, domain breach).
    /// </summary>
    Blocker = 5,

 


}
