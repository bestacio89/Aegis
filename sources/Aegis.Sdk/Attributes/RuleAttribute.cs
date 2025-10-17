using Aegis.Shared.Enums;

namespace Aegis.Sdk.Attributes;

/// <summary>
/// Provides metadata for rules, aiding in reflection-based discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RuleAttribute : Attribute
{
    public string Id { get; }
    public string Name { get; }
    public RuleCategory Category { get; }
    public RuleSeverity Severity { get; }

    public RuleAttribute(string id, string name, RuleCategory category, RuleSeverity severity)
    {
        Id = id;
        Name = name;
        Category = category;
        Severity = severity;
    }
}
