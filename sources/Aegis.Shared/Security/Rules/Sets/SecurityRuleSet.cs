using System.Collections.ObjectModel;

namespace Aegis.Shared.Security.Models.Rules;

/// <summary>
/// Base class for all security rule sets, providing common behavior and metadata.
/// </summary>
public abstract class SecurityRuleSet
{
    /// <summary>Unique registry key for this rule set (e.g., "Application", "IaC").</summary>
    public abstract string Key { get; }

    /// <summary>Human-readable name for UI and reporting.</summary>
    public abstract string Name { get; }

    /// <summary>Short description of the rule set’s purpose.</summary>
    public abstract string Description { get; }

    /// <summary>Collection of rule definitions contained in this set.</summary>
    public abstract IReadOnlyCollection<SecurityRuleDefinition> Rules { get; }

    /// <summary>Returns the rule by ID, if it exists.</summary>
    public SecurityRuleDefinition? GetRule(string id) =>
        Rules.FirstOrDefault(r => r.RuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
}
