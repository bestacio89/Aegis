using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Selects statically declared Android Gradle compile dependency configurations for a
/// derived variant. It never resolves artifacts or evaluates Gradle scripts.
/// </summary>
public sealed class AndroidVariantDependencySelector
{
    private static readonly string[] CompileConfigurationSuffixes =
    [
        "Api",
        "Implementation",
        "CompileOnly"
    ];

    public AndroidVariantDependencySelection Select(
        ProjectModuleContext module,
        string variantName,
        AndroidVariantDependencyScope scope)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantName);

        var diagnostics = new List<AnalysisDiagnostic>();
        var android = module.GradleBuildModel?.Android;
        var build = module.GradleBuildModel;

        if (android is null || build is null)
        {
            diagnostics.Add(CreateDiagnostic(
                "AndroidVariantDependencyModelUnavailable",
                "Android variant dependency selection requires a statically discovered Android Gradle build model.",
                module.Path));
            return Incomplete(variantName, scope, diagnostics);
        }

        var matchingVariants = android.Variants
            .Where(variant => variant.Name.Equals(variantName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingVariants.Length != 1)
        {
            diagnostics.Add(CreateDiagnostic(
                matchingVariants.Length == 0
                    ? "AndroidVariantNotDeclared"
                    : "AndroidVariantIdentityAmbiguous",
                matchingVariants.Length == 0
                    ? $"Android variant '{variantName}' is not present in the statically derived variant model."
                    : $"Android variant '{variantName}' is ambiguous in the statically derived variant model.",
                module.Path));
            return Incomplete(variantName, scope, diagnostics);
        }

        var variant = matchingVariants[0];
        var configurations = GetApplicableConfigurations(variant, scope);
        var selectedDependencies = build.Dependencies
            .Where(dependency => configurations.Contains(dependency.Configuration))
            .ToArray();

        return new AndroidVariantDependencySelection(
            variant.Name,
            scope,
            selectedDependencies,
            diagnostics,
            build.IsComplete && android.IsComplete);
    }

    private static HashSet<string> GetApplicableConfigurations(
        GradleAndroidVariant variant,
        AndroidVariantDependencyScope scope)
    {
        var configurations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCompileConfigurations(configurations, string.Empty);

        foreach (var flavor in variant.ProductFlavors)
            AddCompileConfigurations(configurations, flavor);

        AddCompileConfigurations(configurations, variant.BuildType);
        AddCompileConfigurations(configurations, variant.Name);

        switch (scope)
        {
            case AndroidVariantDependencyScope.Production:
                break;

            case AndroidVariantDependencyScope.UnitTest:
                AddScopedConfigurations(configurations, "test", variant);
                break;

            case AndroidVariantDependencyScope.AndroidTest:
                AddScopedConfigurations(configurations, "androidTest", variant);
                break;

            case AndroidVariantDependencyScope.TestFixtures:
                AddScopedConfigurations(configurations, "testFixtures", variant);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported Android dependency scope.");
        }

        return configurations;
    }

    private static void AddScopedConfigurations(
        HashSet<string> configurations,
        string prefix,
        GradleAndroidVariant variant)
    {
        AddCompileConfigurations(configurations, prefix);
        foreach (var flavor in variant.ProductFlavors)
            AddCompileConfigurations(configurations, $"{prefix}{Capitalize(flavor)}");

        AddCompileConfigurations(configurations, $"{prefix}{Capitalize(variant.BuildType)}");
        AddCompileConfigurations(configurations, $"{prefix}{Capitalize(variant.Name)}");
    }

    private static void AddCompileConfigurations(HashSet<string> configurations, string prefix)
    {
        foreach (var suffix in CompileConfigurationSuffixes)
            configurations.Add($"{prefix}{suffix}");
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static AndroidVariantDependencySelection Incomplete(
        string variantName,
        AndroidVariantDependencyScope scope,
        IReadOnlyList<AnalysisDiagnostic> diagnostics) =>
        new(variantName, scope, Array.Empty<GradleBuildModel.Dependency>(), diagnostics, false);

    private static AnalysisDiagnostic CreateDiagnostic(string code, string message, string? target) =>
        new()
        {
            Code = code,
            Component = "Android",
            Source = "Gradle dependency selection",
            Message = message,
            Target = target,
            AffectsCoverage = true
        };
}
