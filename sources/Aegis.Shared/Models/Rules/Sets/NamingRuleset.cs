using Aegis.Shared.Enums;
using Aegis.Shared.Rules;

namespace Aegis.Shared.Models.Rules.Sets.Naming;

/// <summary>
/// 🏷️ Enforces consistent and standardized naming conventions across classes,
/// interfaces, methods, variables, and files to enhance readability and maintainability.
/// </summary>
public static class NamingRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        // ===============================================================
        // 🏷️ Class and Interface Naming
        // ===============================================================
        new RuleDefinition
        {
            Id = "AEG-NAM-R1",
            Name = "Class Naming Compliance Too Low",
            Category = nameof(RuleCategory.Naming),
            MetricKey = "ClassNamingComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = RuleSeverity.Medium,
            Recommendation = "Ensure class names use PascalCase and are meaningful nouns (e.g., UserService, OrderRepository)."
        },
        new RuleDefinition
        {
            Id = "AEG-NAM-R2",
            Name = "Interface Naming Missing 'I' Prefix",
            Category = nameof(RuleCategory.Naming),
            MetricKey = "InterfacePrefixViolationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Low,
            Recommendation = "Prefix interface names with 'I' (e.g., IRepository, ICacheProvider)."
        },

        // ===============================================================
        // 🧩 Method Naming
        // ===============================================================
        new RuleDefinition
        {
            Id = "AEG-NAM-R3",
            Name = "Method Naming Style Non-Compliant",
            Category = nameof(RuleCategory.Naming),
            MetricKey = "MethodNamingComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = RuleSeverity.Medium,
            Recommendation = "Use camelCase for method names (e.g., calculateTotal, getUserProfile)."
        },

        // ===============================================================
        // 🧮 Variable and Constant Naming
        // ===============================================================
        new RuleDefinition
        {
            Id = "AEG-NAM-R4",
            Name = "Variable Naming Style Non-Compliant",
            Category = nameof(RuleCategory.Naming),
            MetricKey = "VariableNamingComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = RuleSeverity.Medium,
            Recommendation = "Use camelCase for variables and UPPER_CASE only for constants if explicitly allowed."
        },

        // ===============================================================
        // 📂 File and Pluralization
        // ===============================================================
        new RuleDefinition
        {
            Id = "AEG-NAM-R5",
            Name = "Pluralization Violation in File Naming",
            Category = nameof(RuleCategory.Naming),
            MetricKey = "FilePluralizationViolation",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Low,
            Recommendation = "Avoid pluralized file names for singular services/controllers (e.g., 'UserService.cs', not 'UsersService.cs')."
        },

        // ===============================================================
        // 🧭 Overall Compliance
        // ===============================================================
        new RuleDefinition
        {
            Id = "AEG-NAM-R6",
            Name = "Global Naming Compliance Low",
            Category = nameof(RuleCategory.Naming),
            MetricKey = "OverallNamingComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Critical,
            Recommendation = "Improve global naming consistency across classes, methods, and variables for architectural clarity."
        }
    };
}
