using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// 🏷️ Enforces consistent and standardized naming conventions across classes,
/// interfaces, methods, variables, and files to enhance readability and maintainability.
/// </summary>
public static class NamingRuleset
{
    public static IEnumerable<ArchitectureRuleDefinition> Get() => new[]
    {
        // ===============================================================
        // 🏷️ Class and Interface Naming
        // ===============================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R1",
            Name = "Class Naming Compliance Too Low",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "ClassNamingComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure class names use PascalCase and are meaningful nouns (e.g., UserService, OrderRepository)."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R2",
            Name = "Interface Naming Missing 'I' Prefix",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "InterfacePrefixViolationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Prefix interface names with 'I' (e.g., IRepository, ICacheProvider)."
        },

        // ===============================================================
        // 🧩 Method Naming
        // ===============================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R3",
            Name = "Method Naming Style Non-Compliant",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "MethodNamingComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Use Pascal for method names (e.g., calculateTotal, getUserProfile)."
        },

        // ===============================================================
        // 🧮 Variable and Constant Naming
        // ===============================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R4",
            Name = "Variable Naming Style Non-Compliant",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "VariableNamingComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Use camelCase for variables and UPPER_CASE only for constants if explicitly allowed."
        },

        // ===============================================================
        // 📂 File and Pluralization
        // ===============================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R5",
            Name = "Pluralization Violation in File Naming",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "FilePluralizationViolation",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Avoid pluralized file names for singular services/controllers (e.g., 'UserService.cs', not 'UsersService.cs')."
        },

        // ===============================================================
        // 🧭 Overall Compliance
        // ===============================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R6",
            Name = "Global Naming Compliance Low",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "OverallNamingComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Critical,
            Recommendation = "Improve global naming consistency across classes, methods, and variables for architectural clarity."
        }
    };
}
