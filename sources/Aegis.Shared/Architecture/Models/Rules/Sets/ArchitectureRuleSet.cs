using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// 🧭 Architecture governance rule set — enforcing modular boundaries,
/// dependency hygiene, documentation, and overall system consistency.
/// </summary>
public static class ArchitectureRuleset
{
    public static IEnumerable<ArchitectureRuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 🔄 Circular Dependency Control
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R1",
            Scope = RuleEvaluationScope.Project,
            Name = "Circular Dependencies Detected",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "CircularDependencyCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Blocker,
            Recommendation = "Remove circular references between layers or modules using interfaces, messaging, or dependency inversion."
        },

        // ==========================================================
        // 🧩 Layer Isolation & Forbidden References
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R2",
            Scope = RuleEvaluationScope.Project,
            Name = "Layer Isolation Breach",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "LayerIsolationCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Verify that API → Application → Domain → Infrastructure dependency direction is respected."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R3",
            Scope = RuleEvaluationScope.Project,
            Name = "Forbidden Layer Reference Detected",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "ForbiddenReferenceCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Remove direct dependencies from higher layers (e.g., Infrastructure) into Domain or Application layers."
        },

        // ==========================================================
        // 🧭 Cross-Domain / Infrastructure Rules
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R4",
            Scope = RuleEvaluationScope.Project,
            Name = "Cross-Domain Leakage Detected",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "CrossDomainLeakageCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Avoid exposing internal Domain entities to external layers. Use DTOs or mappers to isolate boundaries."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R5",
            Scope = RuleEvaluationScope.Project,
            Name = "Infrastructure Dependency Inversion Broken",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "InfrastructureInversionCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure Domain does not depend on Infrastructure. Apply Dependency Inversion with interfaces or adapters."
        },

        // ==========================================================
        // ⚙️ Controller & API Consistency
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R6",
            Scope = RuleEvaluationScope.Project,
            Name = "Controller Logic Leakage",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "ControllerLogicDensity",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.35,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Reduce business logic inside controllers. Move to Application or Domain services."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R7",
            Scope = RuleEvaluationScope.Project,
            Name = "API Consistency Below Standard",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "ApiConsistencyIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Info,
            Recommendation = "Ensure consistent HTTP verbs, route casing, and pluralization across controllers."
        },

        // ==========================================================
        // 🧱 Structural Health & Cohesion
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R8",
            Scope = RuleEvaluationScope.Project,
            Name = "God Class Found in Core Layer",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "GodClassDensity",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.25,
            Severity = ArchitectureRuleSeverity.Critical,
            Recommendation = "Split large classes exceeding method/field ratios into smaller, cohesive units."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R9",
            Scope = RuleEvaluationScope.Project,
            Name = "Layer Cohesion Too Low",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "LayerCohesionIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 75,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Increase cohesion by consolidating related operations and minimizing cross-layer chatter."
        },

        // ==========================================================
        // 🔐 Security & Documentation
        // ==========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R10",
            Scope = RuleEvaluationScope.Project,
            Name = "Security Compliance Low",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "SecurityComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Review encryption policies, API key management, and secure transport (HTTPS/TLS) enforcement."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R11",
            Scope = RuleEvaluationScope.Project,
            Name = "Documentation Coverage Below Standard",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "DocumentationCoverage",
            Operator = ComparisonOperator.LessThan,
            Threshold = 70,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Improve architectural documentation, add UML/C4 diagrams, and ensure code-level XML/Swagger comments."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-ARCH-R12",
            Scope = RuleEvaluationScope.Project,
            Name = "Node Coupling Too High",
            Category = nameof(ArchitectureRuleCategory.Architecture),
            MetricKey = "NodeCouplingIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.6,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Reduce inter-module dependencies through events, domain interfaces, or mediator patterns."
        }
    };
}
