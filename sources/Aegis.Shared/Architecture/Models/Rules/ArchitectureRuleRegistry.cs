
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules.Sets;

namespace Aegis.Shared.Architecture.Models.Rules;

/// <summary>
/// Central registry and validation boundary for all Aegis architecture rules.
///
/// The expected inventory is deliberately explicit. Any intentional addition or
/// removal of a rule must update this inventory in the same change.
/// </summary>
public static class ArchitectureRuleRegistry
{
    private static readonly Lazy<IReadOnlyList<ArchitectureRuleDefinition>> AllRules =
        new(LoadAllRules, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly HashSet<string> ExpectedRuleIds = new(
        new[]
        {
            // Architecture: 12
            "AEG-ARCH-R1",
            "AEG-ARCH-R2",
            "AEG-ARCH-R3",
            "AEG-ARCH-R4",
            "AEG-ARCH-R5",
            "AEG-ARCH-R6",
            "AEG-ARCH-R7",
            "AEG-ARCH-R8",
            "AEG-ARCH-R9",
            "AEG-ARCH-R10",
            "AEG-ARCH-R11",
            "AEG-ARCH-R12",

            // Backend: 5
            "AEG-BACK-R1",
            "AEG-BACK-R2",
            "AEG-BACK-R3",
            "AEG-BACK-R4",
            "AEG-BACK-R5",

            // Dependencies: 4
            "AEG-DEPS-R1",
            "AEG-DEPS-R2",
            "AEG-DEPS-R3",
            "AEG-DEPS-R4",

            // Design patterns: 24
            "AEG-DES-DEC001",
            "AEG-DES-DEC002",
            "AEG-DES-FAC001",
            "AEG-DES-FAC002",
            "AEG-DES-REPO001",
            "AEG-DES-REPO002",
            "AEG-DES-BLD001",
            "AEG-DES-BLD002",
            "AEG-DES-FACR001",
            "AEG-DES-FACR002",
            "AEG-DES-CMD001",
            "AEG-DES-CMD002",
            "AEG-DES-MEDI001",
            "AEG-DES-MEDI002",
            "AEG-DES-STRAT001",
            "AEG-DES-STRAT002",
            "AEG-DES-OBS001",
            "AEG-DES-OBS002",
            "AEG-DES-SING001",
            "AEG-DES-SING002",
            "AEG-DES-GOD001",
            "AEG-DES-ADM001",
            "AEG-DES-CIRC001",
            "AEG-DES-OVER001",

            // Frontend: 11
            "AEG-FRONT-R1",
            "AEG-FRONT-R2",
            "AEG-FRONT-R3",
            "AEG-FRONT-R4",
            "AEG-FRONT-R5",
            "AEG-FRONT-R6",
            "AEG-FRONT-R7",
            "AEG-FRONT-R8",
            "AEG-FRONT-R9",
            "AEG-FRONT-R10",
            "AEG-FRONT-R11",

            // Infrastructure: 3
            "AEG-INF-R1",
            "AEG-INF-R2",
            "AEG-INF-R3",

            // Naming: 6
            "AEG-NAM-R1",
            "AEG-NAM-R2",
            "AEG-NAM-R3",
            "AEG-NAM-R4",
            "AEG-NAM-R5",
            "AEG-NAM-R6",

            // Performance: 4
            "AEG-PERF-R1",
            "AEG-PERF-R2",
            "AEG-PERF-R3",
            "AEG-PERF-R4",

            // Persistence and transactions: 4
            "AEG-TXN-R1",
            "AEG-TXN-R2",
            "AEG-TXN-R3",
            "AEG-TXN-R4"
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All registered rules. Initialization fails if the inventory is invalid.
    /// </summary>
    public static IReadOnlyList<ArchitectureRuleDefinition> All =>
        AllRules.Value;

    /// <summary>
    /// Returns rules belonging to a category.
    /// </summary>
    public static IEnumerable<ArchitectureRuleDefinition> GetByCategory(
        ArchitectureRuleCategory category) =>
        All.Where(rule =>
            string.Equals(
                rule.Category,
                category.ToString(),
                StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds a rule by its identifier.
    /// </summary>
    public static ArchitectureRuleDefinition? GetById(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return All.FirstOrDefault(rule =>
            string.Equals(
                rule.Id,
                id,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Validates the loaded rule inventory against the authoritative expected IDs
    /// and verifies that every definition is structurally valid.
    ///
    /// This method is public so automated tests can validate alternative rule
    /// collections without initializing the production registry.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        IReadOnlyCollection<ArchitectureRuleDefinition> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var errors = new List<string>();
        var actualIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            if (rule is null)
            {
                errors.Add("The rule collection contains a null definition.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                errors.Add("A rule has an empty or whitespace-only ID.");
            }
            else if (!actualIds.Add(rule.Id))
            {
                errors.Add($"Duplicate rule ID detected: '{rule.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
                errors.Add($"Rule '{rule.Id}' has no name.");

            if (string.IsNullOrWhiteSpace(rule.Category) ||
                !Enum.TryParse<ArchitectureRuleCategory>(
                    rule.Category,
                    ignoreCase: true,
                    out _))
            {
                errors.Add(
                    $"Rule '{rule.Id}' has an invalid category: '{rule.Category}'.");
            }

            if (string.IsNullOrWhiteSpace(rule.MetricKey))
                errors.Add($"Rule '{rule.Id}' has no metric key.");

            if (!double.IsFinite(rule.Threshold))
            {
                errors.Add(
                    $"Rule '{rule.Id}' has a non-finite threshold: {rule.Threshold}.");
            }

            if (!Enum.IsDefined(rule.Operator))
            {
                errors.Add(
                    $"Rule '{rule.Id}' has an invalid comparison operator.");
            }

            if (!Enum.IsDefined(rule.Severity))
            {
                errors.Add(
                    $"Rule '{rule.Id}' has an invalid severity.");
            }

            if (string.IsNullOrWhiteSpace(rule.Recommendation))
            {
                errors.Add(
                    $"Rule '{rule.Id}' has no recommendation.");
            }
        }

        var missingIds = ExpectedRuleIds
            .Except(actualIds, StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var unexpectedIds = actualIds
            .Except(ExpectedRuleIds, StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingIds.Length > 0)
        {
            errors.Add(
                $"Missing expected rule IDs ({missingIds.Length}): " +
                string.Join(", ", missingIds));
        }

        if (unexpectedIds.Length > 0)
        {
            errors.Add(
                $"Unexpected rule IDs ({unexpectedIds.Length}): " +
                string.Join(", ", unexpectedIds));
        }

        if (rules.Count != ExpectedRuleIds.Count)
        {
            errors.Add(
                $"Expected {ExpectedRuleIds.Count} registered rules, " +
                $"but received {rules.Count}.");
        }

        return errors.AsReadOnly();
    }

    private static IReadOnlyList<ArchitectureRuleDefinition> LoadAllRules()
    {
        var rules = new List<ArchitectureRuleDefinition>();

        // Core architecture and design patterns.
        rules.AddRange(ArchitectureRuleset.Get());
        rules.AddRange(DesignPatternRuleset.Get());

        // Dependency management and naming.
        rules.AddRange(DependencyRuleset.Get());
        rules.AddRange(NamingRuleset.Get());

        // Domain-specific and quality rule sets.
        rules.AddRange(BackendRuleset.Get());
        rules.AddRange(FrontendRuleset.Get());
        rules.AddRange(PersistenceRuleset.Get());
        rules.AddRange(PerformanceRuleset.Get());
        rules.AddRange(InfrastructureRuleset.Get());

        var errors = Validate(rules);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Architecture rule registry validation failed:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    errors.Select(error => $" - {error}")));
        }

        return rules.AsReadOnly();
    }
}
