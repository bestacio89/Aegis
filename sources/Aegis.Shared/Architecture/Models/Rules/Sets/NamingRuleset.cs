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
            Scope = RuleEvaluationScope.File,
            Name = "Type Naming Compliance Too Low",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "TypeNamingCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure class names use PascalCase and are meaningful nouns (e.g., UserService, OrderRepository)."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R2",
            Scope = RuleEvaluationScope.File,
            Name = "Interface Naming Compliance Too Low",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "InterfaceNamingCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Apply the language-appropriate interface naming convention consistently."
        },

        // ===============================================================
        // 🧩 Method Naming
        // ===============================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R3",
            Scope = RuleEvaluationScope.File,
            Name = "Method Naming Style Non-Compliant",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "MethodNamingCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Use the language-specific method casing convention (PascalCase for C#, camelCase for Java and JavaScript/TypeScript, and snake_case for Python)."
        },

        // ===============================================================
        // 🧮 Variable and Constant Naming
        // ===============================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R4",
            Scope = RuleEvaluationScope.File,
            Name = "Variable Naming Style Non-Compliant",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "VariableNamingCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Use the language-specific local-variable, parameter, and field naming convention consistently."
        },

        // ===============================================================
        // 📂 File and Pluralization
        // ===============================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R5",
            Scope = RuleEvaluationScope.File,
            Name = "File Naming Compliance Too Low",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "FileNamingCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Use consistent, language-appropriate file naming conventions."
        },

        // ===============================================================
        // 🧭 Overall Compliance
        // ===============================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R6",
            Scope = RuleEvaluationScope.File,
            Name = "Global Naming Compliance Low",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "NamingComplianceScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            // Naming style is maintainability guidance, not a critical
            // architecture-boundary failure.
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Improve global naming consistency across classes, methods, and variables for architectural clarity."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R7",
            Scope = RuleEvaluationScope.File,
            Name = "Constant Naming Style Non-Compliant",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "ConstantNamingCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Use language-specific constant naming conventions; C# constants commonly use PascalCase and uppercase snake case is supported where explicitly required."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R8",
            Scope = RuleEvaluationScope.File,
            Name = "Field Naming Style Non-Compliant",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "FieldNamingCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Apply visibility- and framework-appropriate field naming conventions; keep private fields distinct from public fields and respect framework-specific prefixes."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-NAM-R9",
            Scope = RuleEvaluationScope.File,
            Name = "Property Naming Style Non-Compliant",
            Category = nameof(ArchitectureRuleCategory.Naming),
            MetricKey = "PropertyNamingCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Use PascalCase for C# property names and preserve the language-specific convention for other supported languages."
        }
    };
}
