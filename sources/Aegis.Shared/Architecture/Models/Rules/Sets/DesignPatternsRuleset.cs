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
            Name = "Decorator Delegation Compliance Low",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "DecoratorPatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure decorators delegate calls to the wrapped component and respect interface boundaries."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-DEC002",
            Name = "Decorator Self-Reference Detected",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "DecoratorSelfReferenceCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Avoid decorators referencing themselves or causing recursive loops."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-FAC001",
            Name = "Facade Exposure Too Broad",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "FacadePublicMethodCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 20,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Simplify Facades exposing too many operations; group sub-facades or apply CQRS."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-FAC002",
            Name = "Facade Missing Encapsulation",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "FacadePatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure the Facade properly hides subsystem complexity and external dependencies."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-REPO001",
            Name = "Repository Abstraction Missing",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "RepositoryInterfaceComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Define interfaces for repositories and separate persistence from domain logic."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-REPO002",
            Name = "Repository Performing External I/O",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "RepositoryIOOperationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Repositories should not perform file/network operations directly. Delegate to infrastructure services."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-BLD001",
            Name = "Builder Pattern Incomplete Implementation",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "BuilderPatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure all builders expose a Build() method and follow fluent chaining conventions."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-BLD002",
            Name = "Builder Mutability Violation",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "BuilderMutabilityViolationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Builder steps should return new immutable states or self for fluent usage."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-FACR001",
            Name = "Factory Pattern Compliance Low",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "FactoryPatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Ensure factory classes centralize object creation and avoid business logic within factories."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-FACR002",
            Name = "Factory Direct Instantiation Detected",
            Category = nameof(ArchitectureRuleCategory.Design),
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
            Name = "Command Missing Corresponding Handler",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "CommandHandlerPairingIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Each Command should have a dedicated Handler; ensure one-to-one mapping."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-CMD002",
            Name = "Command Handler Overload Detected",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "CommandHandlerOverloadCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 1,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Handlers should remain focused; split overloaded handlers into smaller units."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-MEDI001",
            Name = "Mediator Centralization Too High",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "MediatorCouplingIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.6,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Mediator should not become a God Object; delegate responsibilities appropriately."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-MEDI002",
            Name = "Mediator Direct Handler Calls Detected",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "MediatorDirectCallCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Handlers should be invoked indirectly through mediator dispatch, not directly."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-STRAT001",
            Name = "Strategy Pattern Conditional Selection",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "StrategyConditionalCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Use polymorphism to replace conditional strategy selection logic."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-STRAT002",
            Name = "Strategy Compliance Low",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "StrategyPatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Ensure consistent strategy injection and interface segregation."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-OBS001",
            Name = "Observer Missing Unsubscribe Mechanism",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "ObserverUnsubscribeMissingCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.High,
            Recommendation = "Ensure observers can detach safely; always provide Unsubscribe or Dispose methods."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-OBS002",
            Name = "Observer Tight Coupling Detected",
            Category = nameof(ArchitectureRuleCategory.Design),
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
            Name = "Singleton Thread Safety Missing",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "SingletonThreadSafetyIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Implement proper thread-safe initialization (e.g., Lazy<T> or double-check locking)."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-SING002",
            Name = "Singleton Global State Detected",
            Category = nameof(ArchitectureRuleCategory.Design),
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
            Name = "God Class Detected",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "GodClassDensity",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.25,
            Severity = ArchitectureRuleSeverity.Critical,
            Recommendation = "Split classes exceeding method/field thresholds. Apply SRP and DDD boundaries."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-ADM001",
            Name = "Anemic Domain Model Detected",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "AnemicDomainModelIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.3,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Move business logic into entities. Avoid passive data structures."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-CIRC001",
            Name = "Circular Pattern Reference Detected",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "PatternCircularReferenceCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = ArchitectureRuleSeverity.Medium,
            Recommendation = "Avoid cross-references between patterns (e.g., Decorator wrapping Mediator)."
        },
        new ArchitectureRuleDefinition
        {
            Id = "AEG-DES-OVER001",
            Name = "Pattern Overuse Detected",
            Category = nameof(ArchitectureRuleCategory.Design),
            MetricKey = "PatternOveruseIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.5,
            Severity = ArchitectureRuleSeverity.Low,
            Recommendation = "Simplify abstractions; prefer clarity and intent over excessive pattern layering."
        }
    };
}
