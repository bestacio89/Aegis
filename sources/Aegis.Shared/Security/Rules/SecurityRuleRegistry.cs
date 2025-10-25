using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Rules;

/// <summary>Registry of available security rule definitions (metadata catalog).</summary>
public sealed class SecurityRuleRegistry
{
    private readonly Dictionary<string, SecurityRuleDefinition> _byId = new(StringComparer.OrdinalIgnoreCase);

    public void Register(SecurityRuleDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.RuleId))
            throw new ArgumentException("RuleId is required", nameof(def));
        _byId[def.RuleId] = def;
    }

    public void RegisterRange(IEnumerable<SecurityRuleDefinition> defs)
    {
        foreach (var d in defs) Register(d);
    }

    public SecurityRuleDefinition? GetById(string ruleId) =>
        _byId.TryGetValue(ruleId, out var def) ? def : null;

    public IReadOnlyCollection<SecurityRuleDefinition> GetAll() => _byId.Values.ToArray();

    public IEnumerable<SecurityRuleDefinition> ByCategory(SecurityCategory category) =>
        _byId.Values.Where(d => d.Category == category);

    public IEnumerable<SecurityRuleDefinition> BySeverity(SecuritySeverity severity) =>
        _byId.Values.Where(d => d.Severity == severity);
}
