using Aegis.Shared.Enums;
using Aegis.Shared.Models.Rules.Sets;
using Aegis.Shared.Models.Rules.Sets.Dependency;
using Aegis.Shared.Models.Rules.Sets.Naming;
using Aegis.Shared.Rules;
using Aegis.Shared.Rules.Sets.BackEnd;
using Aegis.Shared.Rules.Sets.DesignPatterns;
using Aegis.Shared.Rules.Sets.FrontEnd;
using Aegis.Shared.Rules.Sets.Performance;
using Aegis.Shared.Rules.Sets.Persistence;
using France.Common.Extensions;

namespace Aegis.Shared.Models.Rules;

/// <summary>
/// 🧠 Central registry that aggregates all rule definitions from modular rule sets.
/// Acts as the single source of truth for all governance rules available to Aegis.
/// </summary>
public static class RuleRegistry
{
    private static readonly Lazy<IReadOnlyList<RuleDefinition>> _allRules = new(LoadAllRules);

    /// <summary>
    /// Returns all available rules across all categories.
    /// </summary>
    public static IReadOnlyList<RuleDefinition> All => _allRules.Value;

    /// <summary>
    /// Returns all rules filtered by category.
    /// </summary>
    public static IEnumerable<RuleDefinition> GetByCategory(RuleCategory category) =>
        All.Where(r => string.Equals(r.Category, category.ToString(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns a rule definition by ID.
    /// </summary>
    public static RuleDefinition? GetById(string id) =>
        All.FirstOrDefault(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Loads all rule sets into memory.
    /// Automatically includes only those assemblies and sets that are present at runtime.
    /// </summary>
    private static IReadOnlyList<RuleDefinition> LoadAllRules()
    {
        var list = new List<RuleDefinition>();

        // Core architecture & design pattern rules
        list.AddRangeSafe(ArchitectureRuleset.Get());
        list.AddRangeSafe(DesignPatternRuleset.Get());

        // Structural and dependency rules
        list.AddRangeSafe(DependencyRuleset.Get());
        list.AddRangeSafe(NamingRuleset.Get());

        // Optional rule sets (loaded only if present)
        TryAddRuleset(list, "Aegis.Shared.Rules.Sets.BackEnd.BackendRuleset");
        TryAddRuleset(list, "Aegis.Shared.Rules.Sets.FrontEnd.FrontendRuleset");
        TryAddRuleset(list, "Aegis.Shared.Rules.Sets.Persistence.PersistenceRuleset");
        TryAddRuleset(list, "Aegis.Shared.Rules.Sets.Performance.PerformanceRuleset");

        return list.AsReadOnly();
    }

    /// <summary>
    /// Safely attempts to load a ruleset type by its full name.
    /// </summary>
    private static void TryAddRuleset(ICollection<RuleDefinition> target, string fullTypeName)
    {
        var type = Type.GetType(fullTypeName, throwOnError: false);
        if (type == null) return;

        var getMethod = type.GetMethod("Get", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (getMethod == null) return;

        if (getMethod.Invoke(null, null) is IEnumerable<RuleDefinition> rules)
            target.AddRange(rules);
    }

    /// <summary>
    /// Helper extension to avoid null explosions during optional set loading.
    /// </summary>
    private static void AddRangeSafe(this ICollection<RuleDefinition> target, IEnumerable<RuleDefinition>? source)
    {
        if (source == null) return;
        foreach (var rule in source)
            target.Add(rule);
    }
}
