using Aegis.Shared.Enums;
using Aegis.Shared.Rules;

namespace Aegis.Shared.Models.Rules.Sets;

/// <summary>
/// 🧭 Architecture governance rule set — enforcing modular boundaries,
/// dependency hygiene, documentation, and overall system consistency.
/// </summary>
public static class ArchitectureRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        // ==========================================================
        // 🔄 Circular Dependency Control
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-ARCH-R1",
            Name = "Circular Dependencies Detected",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "CircularDependencyCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Critical,
            Recommendation = "Remove circular references between layers or modules using interfaces, messaging, or dependency inversion."
        },

        // ==========================================================
        // 🧩 Layer Isolation & Forbidden References
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-ARCH-R2",
            Name = "Layer Isolation Breach",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "LayerIsolationCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = RuleSeverity.Warning,
            Recommendation = "Verify that API → Application → Domain → Infrastructure dependency direction is respected."
        },
        new RuleDefinition
        {
            Id = "AEG-ARCH-R3",
            Name = "Forbidden Layer Reference Detected",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "ForbiddenReferenceCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Error,
            Recommendation = "Remove direct dependencies from higher layers (e.g., Infrastructure) into Domain or Application layers."
        },

        // ==========================================================
        // 🧭 Cross-Domain / Infrastructure Rules
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-ARCH-R4",
            Name = "Cross-Domain Leakage Detected",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "CrossDomainLeakageCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Warning,
            Recommendation = "Avoid exposing internal Domain entities to external layers. Use DTOs or mappers to isolate boundaries."
        },
        new RuleDefinition
        {
            Id = "AEG-ARCH-R5",
            Name = "Infrastructure Dependency Inversion Broken",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "InfrastructureInversionCompliance",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = RuleSeverity.Warning,
            Recommendation = "Ensure Domain does not depend on Infrastructure. Apply Dependency Inversion with interfaces or adapters."
        },

        // ==========================================================
        // ⚙️ Controller & API Consistency
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-ARCH-R6",
            Name = "Controller Logic Leakage",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "ControllerLogicDensity",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.35,
            Severity = RuleSeverity.Warning,
            Recommendation = "Reduce business logic inside controllers. Move to Application or Domain services."
        },
        new RuleDefinition
        {
            Id = "AEG-ARCH-R7",
            Name = "API Consistency Below Standard",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "ApiConsistencyIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Info,
            Recommendation = "Ensure consistent HTTP verbs, route casing, and pluralization across controllers."
        },

        // ==========================================================
        // 🧱 Structural Health & Cohesion
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-ARCH-R8",
            Name = "God Class Found in Core Layer",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "GodClassDensity",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.25,
            Severity = RuleSeverity.Critical,
            Recommendation = "Split large classes exceeding method/field ratios into smaller, cohesive units."
        },
        new RuleDefinition
        {
            Id = "AEG-ARCH-R9",
            Name = "Layer Cohesion Too Low",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "LayerCohesionIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 75,
            Severity = RuleSeverity.Warning,
            Recommendation = "Increase cohesion by consolidating related operations and minimizing cross-layer chatter."
        },

        // ==========================================================
        // 🔐 Security & Documentation
        // ==========================================================
        new RuleDefinition
        {
            Id = "AEG-ARCH-R10",
            Name = "Security Compliance Low",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "SecurityComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = RuleSeverity.Warning,
            Recommendation = "Review encryption policies, API key management, and secure transport (HTTPS/TLS) enforcement."
        },
        new RuleDefinition
        {
            Id = "AEG-ARCH-R11",
            Name = "Documentation Coverage Below Standard",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "DocumentationCoverage",
            Operator = ComparisonOperator.LessThan,
            Threshold = 70,
            Severity = RuleSeverity.Info,
            Recommendation = "Improve architectural documentation, add UML/C4 diagrams, and ensure code-level XML/Swagger comments."
        },
        new RuleDefinition
        {
            Id = "AEG-ARCH-R12",
            Name = "Node Coupling Too High",
            Category = nameof(RuleCategory.Architecture),
            MetricKey = "NodeCouplingIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.6,
            Severity = RuleSeverity.Warning,
            Recommendation = "Reduce inter-module dependencies through events, domain interfaces, or mediator patterns."
        }
    };
}
