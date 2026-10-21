using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models.Rules;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// 🧩 Comprehensive rule set for design pattern governance across all structural,
/// behavioral, and creational patterns.
/// </summary>
public static class DesignPatternRuleset
{
    public static IEnumerable<ArchitectureRuleDefinition> Get() => new[]
    {
        // =========================================================
        // 🧱 STRUCTURAL PATTERNS
        // =========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-DEC001",
            Scope = RuleEvaluationScope.File,
            Name = "Decorator Delegation Compliance Low",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "DecoratorComplianceScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure decorators delegate calls to the wrapped component and respect interface boundaries."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-DEC002",
            Scope = RuleEvaluationScope.File,
            Name = "Decorator Self-Reference Detected",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "DecoratorSelfReferenceCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Avoid decorators referencing themselves or causing recursive loops."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-FAC001",
            Scope = RuleEvaluationScope.File,
            Name = "Facade Exposure Too Broad",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "PublicMethodCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 20,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Simplify Facades exposing too many operations; group sub-facades or apply CQRS."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-FAC002",
            Scope = RuleEvaluationScope.File,
            Name = "Facade Public Surface Too Broad",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "PublicSurfaceRatio",
            // Producer emits publicMethodCount / configured maximum (a ratio),
            // not a percentage. Values above 1.0 exceed the configured limit.
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 1.0,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure the Facade properly hides subsystem complexity and external dependencies."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-REPO001",
            Scope = RuleEvaluationScope.File,
            Name = "Repository Abstraction Missing",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "InterfaceAdherence",
            Operator = ComparisonOperator.LessThan,
            Threshold = 0.85,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Define interfaces for repositories and separate persistence from domain logic."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-REPO002",
            Scope = RuleEvaluationScope.File,
            Name = "Repository Performing External I/O",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "RepositoryIOOperationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Repositories should not perform file/network operations directly. Delegate to infrastructure services."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-BLD001",
            Scope = RuleEvaluationScope.File,
            Name = "Builder Pattern Incomplete Implementation",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "BuilderPatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure all builders expose a Build() method and follow fluent chaining conventions."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-BLD002",
            Scope = RuleEvaluationScope.File,
            Name = "Builder Mutability Violation",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "BuilderMutabilityViolationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Builder steps should return new immutable states or self for fluent usage."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-FACR001",
            Scope = RuleEvaluationScope.File,
            Name = "Factory Pattern Compliance Low",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "FactoryComplianceScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure factory classes centralize object creation and avoid business logic within factories."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-FACR002",
            Scope = RuleEvaluationScope.File,
            Name = "Factory Direct Instantiation Detected",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "FactoryDirectInstantiationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Avoid new() calls outside of factory methods; delegate object creation properly."
        },

        // =========================================================
        // ⚡ BEHAVIORAL PATTERNS
        // =========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-CMD001",
            Scope = RuleEvaluationScope.File,
            Name = "Command Missing Corresponding Handler",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "CommandComplianceScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Each Command should have a dedicated Handler; ensure one-to-one mapping."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-CMD002",
            Scope = RuleEvaluationScope.File,
            Name = "Command Handler Overload Detected",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "CommandHandlerOverloadCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 1,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Handlers should remain focused; split overloaded handlers into smaller units."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-MEDI001",
            Scope = RuleEvaluationScope.File,
            Name = "Mediator Centralization Too High",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "CouplingRisk",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.6,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Mediator should not become a God Object; delegate responsibilities appropriately."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-MEDI002",
            Scope = RuleEvaluationScope.File,
            Name = "Mediator Direct Handler Calls Detected",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "MediatorDirectCallCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Handlers should be invoked indirectly through mediator dispatch, not directly."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-STRAT001",
            Scope = RuleEvaluationScope.File,
            Name = "Strategy Pattern Conditional Selection",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "ConditionalViolationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Use polymorphism to replace conditional strategy selection logic."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-STRAT002",
            Scope = RuleEvaluationScope.File,
            Name = "Strategy Compliance Low",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "StrategyComplianceScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Ensure consistent strategy injection and interface segregation."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-OBS001",
            Scope = RuleEvaluationScope.File,
            Name = "Observer Missing Unsubscribe Mechanism",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "LeakRisk",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Ensure observers can detach safely; always provide Unsubscribe or Dispose methods."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-OBS002",
            Scope = RuleEvaluationScope.File,
            Name = "Observer Tight Coupling Detected",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "ObserverCouplingIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.6,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Reduce coupling between subjects and observers using event buses or weak references."
        },

        // =========================================================
        // 🧠 CREATIONAL PATTERNS
        // =========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-SING001",
            Scope = RuleEvaluationScope.File,
            Name = "Singleton Thread Safety Missing",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "ThreadSafetyScore",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Implement proper thread-safe initialization (e.g., Lazy<T> or double-check locking)."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-SING002",
            Scope = RuleEvaluationScope.File,
            Name = "Singleton Global State Detected",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "SingletonGlobalStateUsage",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Avoid storing mutable global state in singletons; use dependency injection instead."
        },

        // =========================================================
        // 🚨 ANTI-PATTERNS / CODE SMELLS
        // =========================================================
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-GOD001",
            Scope = RuleEvaluationScope.File,
            Name = "God Class Detected",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "GodClassSeverity",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.25,
            Severity = ArchitectureRuleSeverity.Critical,
            Recommendation = "Split classes exceeding method/field thresholds. Apply SRP and DDD boundaries."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-ADM001",
            Scope = RuleEvaluationScope.File,
            Name = "Anemic Domain Model Detected",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "AnemicDomainModelIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.3,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Move business logic into entities. Avoid passive data structures."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-CIRC001",
            Scope = RuleEvaluationScope.File,
            Name = "Circular Pattern Reference Detected",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "PatternCircularReferenceCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Avoid cross-references between patterns (e.g., Decorator wrapping Mediator)."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-OVER001",
            Scope = RuleEvaluationScope.File,
            Name = "Pattern Overuse Detected",
            Category = nameof(ArchitectureRuleCategory.DesignPatterns),
            MetricKey = "PatternOveruseIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.5,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Simplify abstractions; prefer clarity and intent over excessive pattern layering."
        }
    };
}
