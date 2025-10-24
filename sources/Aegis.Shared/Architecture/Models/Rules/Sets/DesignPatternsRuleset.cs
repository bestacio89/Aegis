using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Enums;

namespace Aegis.Shared.Architecture.Models.Rules.Sets;

/// <summary>
/// 🧩 Comprehensive rule set for design pattern governance across all structural,
/// behavioral, and creational patterns.
/// </summary>
public static class DesignPatternRuleset
{
    public static IEnumerable<RuleDefinition> Get() => new[]
    {
        // =========================================================
        // 🧱 STRUCTURAL PATTERNS
        // =========================================================
        new RuleDefinition
        {
            Id = "AEG-DES-DEC001",
            Name = "Decorator Delegation Compliance Low",
            Category = nameof(RuleCategory.Design),
            MetricKey = "DecoratorPatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Medium,
            Recommendation = "Ensure decorators delegate calls to the wrapped component and respect interface boundaries."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-DEC002",
            Name = "Decorator Self-Reference Detected",
            Category = nameof(RuleCategory.Design),
            MetricKey = "DecoratorSelfReferenceCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.High,
            Recommendation = "Avoid decorators referencing themselves or causing recursive loops."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-FAC001",
            Name = "Facade Exposure Too Broad",
            Category = nameof(RuleCategory.Design),
            MetricKey = "FacadePublicMethodCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 20,
            Severity = RuleSeverity.Low,
            Recommendation = "Simplify Facades exposing too many operations; group sub-facades or apply CQRS."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-FAC002",
            Name = "Facade Missing Encapsulation",
            Category = nameof(RuleCategory.Design),
            MetricKey = "FacadePatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = RuleSeverity.Medium,
            Recommendation = "Ensure the Facade properly hides subsystem complexity and external dependencies."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-REPO001",
            Name = "Repository Abstraction Missing",
            Category = nameof(RuleCategory.Design),
            MetricKey = "RepositoryInterfaceComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Medium,
            Recommendation = "Define interfaces for repositories and separate persistence from domain logic."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-REPO002",
            Name = "Repository Performing External I/O",
            Category = nameof(RuleCategory.Design),
            MetricKey = "RepositoryIOOperationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.High,
            Recommendation = "Repositories should not perform file/network operations directly. Delegate to infrastructure services."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-BLD001",
            Name = "Builder Pattern Incomplete Implementation",
            Category = nameof(RuleCategory.Design),
            MetricKey = "BuilderPatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Medium,
            Recommendation = "Ensure all builders expose a Build() method and follow fluent chaining conventions."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-BLD002",
            Name = "Builder Mutability Violation",
            Category = nameof(RuleCategory.Design),
            MetricKey = "BuilderMutabilityViolationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Low,
            Recommendation = "Builder steps should return new immutable states or self for fluent usage."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-FACR001",
            Name = "Factory Pattern Compliance Low",
            Category = nameof(RuleCategory.Design),
            MetricKey = "FactoryPatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Medium,
            Recommendation = "Ensure factory classes centralize object creation and avoid business logic within factories."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-FACR002",
            Name = "Factory Direct Instantiation Detected",
            Category = nameof(RuleCategory.Design),
            MetricKey = "FactoryDirectInstantiationCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Medium,
            Recommendation = "Avoid new() calls outside of factory methods; delegate object creation properly."
        },

        // =========================================================
        // ⚡ BEHAVIORAL PATTERNS
        // =========================================================
        new RuleDefinition
        {
            Id = "AEG-DES-CMD001",
            Name = "Command Missing Corresponding Handler",
            Category = nameof(RuleCategory.Design),
            MetricKey = "CommandHandlerPairingIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 90,
            Severity = RuleSeverity.Medium,
            Recommendation = "Each Command should have a dedicated Handler; ensure one-to-one mapping."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-CMD002",
            Name = "Command Handler Overload Detected",
            Category = nameof(RuleCategory.Design),
            MetricKey = "CommandHandlerOverloadCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 1,
            Severity = RuleSeverity.Low,
            Recommendation = "Handlers should remain focused; split overloaded handlers into smaller units."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-MEDI001",
            Name = "Mediator Centralization Too High",
            Category = nameof(RuleCategory.Design),
            MetricKey = "MediatorCouplingIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.6,
            Severity = RuleSeverity.Medium,
            Recommendation = "Mediator should not become a God Object; delegate responsibilities appropriately."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-MEDI002",
            Name = "Mediator Direct Handler Calls Detected",
            Category = nameof(RuleCategory.Design),
            MetricKey = "MediatorDirectCallCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.High,
            Recommendation = "Handlers should be invoked indirectly through mediator dispatch, not directly."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-STRAT001",
            Name = "Strategy Pattern Conditional Selection",
            Category = nameof(RuleCategory.Design),
            MetricKey = "StrategyConditionalCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Medium,
            Recommendation = "Use polymorphism to replace conditional strategy selection logic."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-STRAT002",
            Name = "Strategy Compliance Low",
            Category = nameof(RuleCategory.Design),
            MetricKey = "StrategyPatternComplianceIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 85,
            Severity = RuleSeverity.Low,
            Recommendation = "Ensure consistent strategy injection and interface segregation."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-OBS001",
            Name = "Observer Missing Unsubscribe Mechanism",
            Category = nameof(RuleCategory.Design),
            MetricKey = "ObserverUnsubscribeMissingCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.High,
            Recommendation = "Ensure observers can detach safely; always provide Unsubscribe or Dispose methods."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-OBS002",
            Name = "Observer Tight Coupling Detected",
            Category = nameof(RuleCategory.Design),
            MetricKey = "ObserverCouplingIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.6,
            Severity = RuleSeverity.Medium,
            Recommendation = "Reduce coupling between subjects and observers using event buses or weak references."
        },

        // =========================================================
        // 🧠 CREATIONAL PATTERNS
        // =========================================================
        new RuleDefinition
        {
            Id = "AEG-DES-SING001",
            Name = "Singleton Thread Safety Missing",
            Category = nameof(RuleCategory.Design),
            MetricKey = "SingletonThreadSafetyIndex",
            Operator = ComparisonOperator.LessThan,
            Threshold = 80,
            Severity = RuleSeverity.Medium,
            Recommendation = "Implement proper thread-safe initialization (e.g., Lazy<T> or double-check locking)."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-SING002",
            Name = "Singleton Global State Detected",
            Category = nameof(RuleCategory.Design),
            MetricKey = "SingletonGlobalStateUsage",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.High,
            Recommendation = "Avoid storing mutable global state in singletons; use dependency injection instead."
        },

        // =========================================================
        // 🚨 ANTI-PATTERNS / CODE SMELLS
        // =========================================================
        new RuleDefinition
        {
            Id = "AEG-DES-GOD001",
            Name = "God Class Detected",
            Category = nameof(RuleCategory.Design),
            MetricKey = "GodClassDensity",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.25,
            Severity = RuleSeverity.Critical,
            Recommendation = "Split classes exceeding method/field thresholds. Apply SRP and DDD boundaries."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-ADM001",
            Name = "Anemic Domain Model Detected",
            Category = nameof(RuleCategory.Design),
            MetricKey = "AnemicDomainModelIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.3,
            Severity = RuleSeverity.Medium,
            Recommendation = "Move business logic into entities. Avoid passive data structures."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-CIRC001",
            Name = "Circular Pattern Reference Detected",
            Category = nameof(RuleCategory.Design),
            MetricKey = "PatternCircularReferenceCount",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            Severity = RuleSeverity.Medium,
            Recommendation = "Avoid cross-references between patterns (e.g., Decorator wrapping Mediator)."
        },
        new RuleDefinition
        {
            Id = "AEG-DES-OVER001",
            Name = "Pattern Overuse Detected",
            Category = nameof(RuleCategory.Design),
            MetricKey = "PatternOveruseIndex",
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0.5,
            Severity = RuleSeverity.Low,
            Recommendation = "Simplify abstractions; prefer clarity and intent over excessive pattern layering."
        }
    };
}
