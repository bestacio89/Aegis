namespace Aegis.Shared.Models.Policies.Architecture;

/// <summary>
/// Defines enforcement and configuration parameters for recognized design patterns
/// used in Aegis architectural evaluation. Each property controls rule strength,
/// naming, and thresholds for pattern-based evaluators.
/// </summary>
public sealed class DesignPatternPolicy
{
    // =========================================================================================================
    // 🎯 GENERAL CONFIGURATION
    // =========================================================================================================

    /// <summary> Enables informational hints from evaluators (e.g., “Consider immutability”). </summary>
    public bool EnablePatternHints { get; set; } = true;

    /// <summary> Allows a class to match multiple pattern evaluators (e.g., Factory + Builder hybrid). </summary>
    public bool AllowPatternOverlap { get; set; } = false;

    /// <summary> Whether to enforce naming conventions for pattern-related classes. </summary>
    public bool RequirePatternSuffix { get; set; } = true;

    /// <summary> Suffixes recognized as valid architectural patterns. </summary>
    public string[] AllowedSuffixes { get; set; } =
        ["Repository", "Service", "Factory", "Handler", "Strategy", "Mediator", "Observer", "Builder"];

    /// <summary> Scaling multipliers for thresholds per architectural layer. </summary>
    public Dictionary<string, double> LayerScaling { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Domain"] = 1.0,          // strictest
        ["Application"] = 1.25,
        ["Infrastructure"] = 1.5,
        ["Api"] = 1.2
    };

    // =========================================================================================================
    // 🧩 REPOSITORY PATTERN
    // =========================================================================================================
    public bool EnforceRepositoryPattern { get; set; } = true;
    public bool RequireRepositoryInterface { get; set; } = true;
    public bool RequireAsyncMethods { get; set; } = false;
    public string RepositorySuffix { get; set; } = "Repository";

    // =========================================================================================================
    // 🔁 SINGLETON PATTERN
    // =========================================================================================================
    public bool EnforceSingletonPattern { get; set; } = true;
    public bool AllowMultipleSingletons { get; set; } = false;
    public bool RequireThreadSafety { get; set; } = true;
    public bool RequireLazyInitialization { get; set; } = true;
    public string SingletonSuffix { get; set; } = "Singleton";

    // =========================================================================================================
    // 🏭 FACTORY PATTERN
    // =========================================================================================================
    public bool EnforceFactoryPattern { get; set; } = true;
    public bool RequireFactoryInterface { get; set; } = false;
    public string FactorySuffix { get; set; } = "Factory";

    /// <summary> Maximum object instantiations (<c>new</c>) per factory before warning. </summary>
    public int MaxFactoryInstantiations { get; set; } = 3;

    // =========================================================================================================
    // 🧱 BUILDER PATTERN
    // =========================================================================================================
    public bool EnforceBuilderPattern { get; set; } = true;
    public bool RequireFluentInterface { get; set; } = true;
    public bool RequireBuildMethod { get; set; } = true;
    public bool SuggestImmutability { get; set; } = true;

    /// <summary> Maximum number of internal mutations before suggesting sub-builders. </summary>
    public int MaxBuilderMutations { get; set; } = 10;

    // =========================================================================================================
    // 🗣️ MEDIATOR PATTERN
    // =========================================================================================================
    public bool EnforceMediatorPattern { get; set; } = false;
    public bool RequireMediatorInterface { get; set; } = false;
    public string MediatorSuffix { get; set; } = "Mediator";

    public int MaxHandlersPerFile { get; set; } = 5;
    public int MaxMediatorCallsPerFile { get; set; } = 15;
    // 🔍 Recognized mediator frameworks (auto-detected in evaluator)
    public Dictionary<string, string[]> MediatorFrameworkHints { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FranzMediator"] = new[]
        {
        "IDispatcher", "ICommandHandler", "IEventHandler", "IQueryHandler", "INotificationHandler"
    },
        ["MediatR"] = new[]
        {
        "IMediator", "IRequestHandler", "INotificationHandler", "IRequest"
    }
    };

    // ✅ Accepted dispatcher method names
    public string[] DispatcherMethods { get; set; } =
    {
    "SendAsync", "PublishAsync", "PublishEventAsync"
};

    // ✅ Accepted mediator method names
    public string[] MediatorMethods { get; set; } =
    {
    "Send", "Publish"
};
    public Dictionary<string, bool> MediatorApplicableLanguages { get; set; } = new()
    {
        ["CSharp"] = true,
        ["Java"] = false,
        ["TypeScript"] = false,
        ["Python"] = false
    };

    public string[] MediatorMethodHints { get; set; } =
    {
       "Send", "SendAsync", "Publish", "PublishAsync", "Dispatch"
    };

    // =========================================================================================================
    // 🪓 COMMAND PATTERN
    // =========================================================================================================
    public bool EnforceCommandPattern { get; set; } = false;
    public string CommandSuffix { get; set; } = "Command";
    public bool RequireUndoCapability { get; set; } = false;
    public bool RequireCommandHandlerPair { get; set; } = true;
    public bool DetectGodCommandObjects { get; set; } = true;
    public int MaxMethodsPerCommand { get; set; } = 3;

    /// <summary> Regex patterns or keywords indicating handler invocation. </summary>
    public string[] HandlerInvocationHints { get; set; } =
        ["Handle", "Execute", "Process", "Dispatch"];

    /// <summary> Forbidden coupling indicators inside Commands. </summary>
    public string[] ForbiddenInCommand { get; set; } =
        ["DbContext", "SqlConnection", "HttpClient", "Repository", "Service"];

    // =========================================================================================================
    // 🧭 STRATEGY PATTERN
    // =========================================================================================================
    public bool EnforceStrategyPattern { get; set; } = false;
    public string StrategySuffix { get; set; } = "Strategy";
    public bool RequireStrategyInterface { get; set; } = true;
    public bool DetectConditionalStrategySwitch { get; set; } = true;
    public bool WarnMultipleStrategiesPerClass { get; set; } = true;

    // =========================================================================================================
    // 👁️ OBSERVER PATTERN
    // =========================================================================================================
    public bool EnforceObserverPattern { get; set; } = false;
    public string ObserverSuffix { get; set; } = "Observer";
    public int MaxSubscribers { get; set; } = 15;
    public bool RequireObserverInterface { get; set; } = true;
    public bool DetectLeakingSubscriptions { get; set; } = true;
    public bool DisallowManualPolling { get; set; } = true;

    // =========================================================================================================
    // 🎭 DECORATOR PATTERN
    // =========================================================================================================
    public bool EnforceDecoratorPattern { get; set; } = true;
    public bool RequireDecoratorConstructorInjection { get; set; } = true;
    public bool RequireDecoratorDelegation { get; set; } = true;

    // =========================================================================================================
    // 🏛️ FACADE PATTERN
    // =========================================================================================================
    public bool EnforceFacadePattern { get; set; } = true;
    public int MaxDependenciesPerFacade { get; set; } = 4;
    public int MaxPublicMethodsPerFacade { get; set; } = 8;
    public int MaxServiceCallsPerFacade { get; set; } = 10;

    // =========================================================================================================
    // 💉 ANEMIC DOMAIN MODEL DETECTION
    // =========================================================================================================
    public bool DetectAnemicDomainModels { get; set; } = true;
    public int MinPropertiesForDomainClass { get; set; } = 3;
    public int MaxAllowedMethodsForAnemic { get; set; } = 1;
    public bool CheckEncapsulation { get; set; } = true;

    // =========================================================================================================
    // ☠️ ANTI-PATTERNS
    // =========================================================================================================
    public bool DetectGodClass { get; set; } = true;
    public bool DetectGodClasses { get; set; } = true;
    public bool DetectCircularDependencies { get; set; } = true;
    public bool DetectAnemicDomainModel { get; set; } = true;

    public int GodClassMethodThreshold { get; set; } = 20;
    public int GodClassLineThreshold { get; set; } = 600;
    public int MaxMethodsPerClass { get; set; } = 20;
    public int MaxLinesPerClass { get; set; } = 600;
    public int AnemicPropertyRatioThreshold { get; set; } = 70; // % properties vs methods
}
