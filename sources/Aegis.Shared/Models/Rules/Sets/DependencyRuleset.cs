using Aegis.Shared.Enums;
using Aegis.Shared.Rules;

namespace Aegis.Shared.Models.Rules.Sets.Dependency;

/// <summary>
/// 🧩 Dependency management and freshness governance ruleset.
/// Ensures version consistency, security posture, and build hygiene.
/// </summary>
public static class DependencyRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 📦 Outdated Dependencies
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-DEPS-R1",
            Name = "Outdated Package Ratio Too High",
            Category = nameof(RuleCategory.Dependency),
            MetricKey = "OutdatedPackageRatio",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.15,
            Severity = RuleSeverity.High,
            Recommendation = "Update outdated packages to maintain compatibility and security."
        },

        // ==========================================================
        // ⏱️ Dependency Freshness
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-DEPS-R2",
            Name = "Dependency Freshness Too Low",
            Category = nameof(RuleCategory.Dependency),
            MetricKey = "DependencyFreshness",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = RuleSeverity.Medium,
            Recommendation = "Ensure dependencies are updated regularly. Aim for 80+ freshness score."
        },

        // ==========================================================
        // 🧹 Unused References
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-DEPS-R3",
            Name = "Unused References Detected",
            Category = nameof(RuleCategory.Dependency),
            MetricKey = "UnusedReferenceCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 3,
            Severity = RuleSeverity.Low,
            Recommendation = "Remove unused dependencies to reduce attack surface and build size."
        },

        // ==========================================================
        // ⚖️ Version Drift
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-DEPS-R4",
            Name = "Version Drift Too Large",
            Category = nameof(RuleCategory.Dependency),
            MetricKey = "MajorVersionDrift",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 1,
            Severity = RuleSeverity.Medium,
            Recommendation = "Align package major versions across projects to prevent runtime mismatches."
        }
    };
}
