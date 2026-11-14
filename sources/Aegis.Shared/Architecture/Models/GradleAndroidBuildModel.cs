namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Static Android Gradle Plugin evidence for one Android module.
/// Values describe literal build declarations; they are not resolved variant or SDK state.
/// </summary>
public sealed class GradleAndroidBuildModel
{
    public string PluginId { get; init; } = string.Empty;

    public string? PluginVersion { get; init; }

    public string ModuleKind { get; init; } = "Unknown";

    /// <summary>BuiltInDefault, BuiltInPlugin, LegacyKotlinAndroidPlugin, Disabled, NotDeclared, or Unknown.</summary>
    public string KotlinIntegration { get; init; } = "Unknown";

    /// <summary>Effective Android Gradle Plugin DSL mode when it can be inferred from the declared AGP version and properties.</summary>
    public bool? NewDslEnabled { get; init; }

    public string? Namespace { get; init; }

    /// <summary>Literal API level or codename; SDK installation is resolved in a later checkpoint.</summary>
    public string? CompileSdk { get; init; }

    public string? CompileSdkPreview { get; init; }

    public string? BuildToolsVersion { get; init; }

    public AndroidDefaultConfig? DefaultConfig { get; init; }

    public AndroidCompileOptions? CompileOptions { get; init; }

    public List<string> FlavorDimensions { get; init; } = [];

    public List<ProductFlavor> ProductFlavors { get; init; } = [];

    public List<BuildType> BuildTypes { get; init; } = [];

    public List<AnalysisDiagnostic> Diagnostics { get; init; } = [];

    public bool IsComplete { get; init; }

    public sealed record AndroidDefaultConfig(
        string? ApplicationId,
        string? MinSdk,
        string? TargetSdk,
        string? TestInstrumentationRunner);

    public sealed record AndroidCompileOptions(
        string? SourceCompatibility,
        string? TargetCompatibility,
        bool? CoreLibraryDesugaringEnabled);

    public sealed record ProductFlavor(
        string Name,
        string? Dimension,
        string? ApplicationIdSuffix,
        string? VersionNameSuffix,
        string? MinSdk,
        string? TargetSdk);

    public sealed record BuildType(
        string Name,
        bool? IsDebuggable,
        bool? IsMinifyEnabled,
        string? ApplicationIdSuffix);
}
