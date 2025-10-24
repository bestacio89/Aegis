using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// 🧩 Dependency management and freshness governance ruleset.
/// Ensures version consistency, security posture, and build hygiene.
/// </summary>
public static class DependencyRuleset
{
    public static IEnumerable<ArchitectureRuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 📦 Outdated Dependencies
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DEPS-R1",
            Name = "Outdated Package Ratio Too High",
            Category = nameof(ArchitectureRuleCategory.Dependency),
            MetricKey = "OutdatedPackageRatio",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.15,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Update outdated packages to maintain compatibility and security."
        },

        // ==========================================================
        // ⏱️ Dependency Freshness
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DEPS-R2",
            Name = "Dependency Freshness Too Low",
            Category = nameof(ArchitectureRuleCategory.Dependency),
            MetricKey = "DependencyFreshness",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure dependencies are updated regularly. Aim for 80+ freshness score."
        },

        // ==========================================================
        // 🧹 Unused References
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DEPS-R3",
            Name = "Unused References Detected",
            Category = nameof(ArchitectureRuleCategory.Dependency),
            MetricKey = "UnusedReferenceCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 3,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Remove unused dependencies to reduce attack surface and build size."
        },

        // ==========================================================
        // ⚖️ Version Drift
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DEPS-R4",
            Name = "Version Drift Too Large",
            Category = nameof(ArchitectureRuleCategory.Dependency),
            MetricKey = "MajorVersionDrift",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 1,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Align package major versions across projects to prevent runtime mismatches."
        }
    };
}
