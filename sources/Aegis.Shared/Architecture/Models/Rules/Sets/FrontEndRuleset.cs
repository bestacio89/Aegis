using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// 🎨 Frontend architecture governance ruleset.
/// Covers SPA frameworks, server-rendered UI, and component-based architectures.
///
/// Supported:
/// - Angular
/// - React
/// - Razor MVC / Razor Pages
/// - Blazor
/// </summary>
public static class FrontendRuleset
{
    public static IEnumerable<ArchitectureRuleDefinition> Get() =>
    [
        // ==========================================================
        // 🅰️ Angular
        // ==========================================================

        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R1",
            Name = "Angular Selector Naming Violation",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "SelectorScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation =
                "Ensure Angular selectors follow approved prefixes and casing conventions."
        },


        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R2",
            Name = "Angular Component Complexity",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "ComplexityScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 70,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation =
                "Split large Angular components and move business logic into services."
        },


        // ==========================================================
        // ⚛️ React
        // ==========================================================

        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R3",
            Name = "React Hook Misuse",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "HookDisciplineScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation =
                "Avoid conditional hooks and respect React hook lifecycle rules."
        },


        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R4",
            Name = "React Component Typing Violation",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "TypingScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation =
                "Use TypeScript interfaces or PropTypes to enforce component contracts."
        },


        // ==========================================================
        // 🟦 Razor MVC / Razor Pages
        // ==========================================================

        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R5",
            Name = "Razor View Business Logic Leakage",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "PresentationSeparationScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation =
                "Move business logic from Razor views into controllers, services, or view models."
        },


        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R6",
            Name = "Razor Entity Exposure",
            Category = nameof(ArchitectureRuleCategory.Security),
            MetricKey = "ViewModelScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation =
                "Do not expose persistence entities directly to views. Use dedicated ViewModels."
        },


        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R7",
            Name = "Razor Data Access Leakage",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "DataIsolationScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 100,
            Severity = ArchitectureRuleSeverity.Critical,
            Recommendation =
                "Views must not access DbContext, repositories, or database infrastructure directly."
        },


        // ==========================================================
        // 🟣 Blazor
        // ==========================================================

        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R8",
            Name = "Blazor Component Complexity",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "ComplexityScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 70,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation =
                "Split large Blazor components and isolate UI responsibilities."
        },


        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R9",
            Name = "Blazor Inline Code Overuse",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "CodeBehindScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation =
                "Move extensive @code blocks into partial classes or dedicated services."
        },


        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R10",
            Name = "Blazor Dependency Construction Violation",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "DependencyInjectionScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 100,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation =
                "Use dependency injection instead of manually creating services inside components."
        },


        // ==========================================================
        // 🧩 Cross Framework
        // ==========================================================

        new ArchitectureRuleDefinition
        {
            Id = "AEG-FRONT-R11",
            Name = "Frontend Component Complexity Too High",
            Category = nameof(ArchitectureRuleCategory.Maintainability),
            MetricKey = "ComplexityScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 60,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation =
                "Reduce component/view complexity through decomposition and separation of concerns."
        }
    ];
}