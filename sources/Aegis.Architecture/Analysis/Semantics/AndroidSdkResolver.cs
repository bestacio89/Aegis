using System.Text.RegularExpressions;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Resolves an Android SDK installation and the platform android.jar declared
/// by static Android build metadata. It never invokes Gradle or downloads SDKs.
/// </summary>
public sealed class AndroidSdkResolver
{
    private readonly string? _configuredSdkRoot;
    private readonly IReadOnlyDictionary<string, string> _environment;

    public AndroidSdkResolver(
        string? configuredSdkRoot = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        _configuredSdkRoot = NormalizeOptionalPath(configuredSdkRoot);
        _environment = environment ?? new Dictionary<string, string>
        {
            ["ANDROID_HOME"] = Environment.GetEnvironmentVariable("ANDROID_HOME") ?? string.Empty,
            ["ANDROID_SDK_ROOT"] = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? string.Empty
        };
    }

    public AndroidSdkResolution Resolve(
        string workspaceRoot,
        string modulePath,
        GradleAndroidBuildModel android)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentNullException.ThrowIfNull(android);

        var diagnostics = new List<AnalysisDiagnostic>();
        var sdkRoot = ResolveSdkRoot(workspaceRoot, modulePath, diagnostics, out var source);
        if (sdkRoot is null)
            return Incomplete(null, null, null, android.CompileSdk, source, diagnostics);

        if (!Directory.Exists(sdkRoot))
        {
            diagnostics.Add(Diagnostic(
                "AndroidSdkRootNotFound",
                "The configured Android SDK root does not exist or is not a directory.",
                sdkRoot));
            return Incomplete(sdkRoot, null, null, android.CompileSdk, source, diagnostics);
        }

        var platform = ResolvePlatformName(android, diagnostics);
        if (platform is null)
            return Incomplete(sdkRoot, null, null, android.CompileSdk, source, diagnostics);

        var platformDirectory = Path.Combine(sdkRoot, "platforms", platform);
        var androidJar = Path.Combine(platformDirectory, "android.jar");
        if (!File.Exists(androidJar))
        {
            diagnostics.Add(Diagnostic(
                "AndroidSdkPlatformNotFound",
                $"The Android SDK platform '{platform}' is not installed or does not contain android.jar.",
                platformDirectory));
            return Incomplete(sdkRoot, platformDirectory, null, android.CompileSdk, source, diagnostics);
        }

        return new AndroidSdkResolution(
            sdkRoot,
            platformDirectory,
            Path.GetFullPath(androidJar),
            android.CompileSdk,
            source,
            true,
            diagnostics);
    }

    private string? ResolveSdkRoot(
        string workspaceRoot,
        string modulePath,
        ICollection<AnalysisDiagnostic> diagnostics,
        out string source)
    {
        source = "Unavailable";
        if (_configuredSdkRoot is not null)
        {
            source = "ExplicitConfiguration";
            return _configuredSdkRoot;
        }

        var localProperties = FindLocalProperties(workspaceRoot, modulePath);
        if (localProperties is not null)
        {
            source = "LocalProperties";
            if (!TryReadSdkDirectory(localProperties, out var sdkDirectory))
            {
                diagnostics.Add(Diagnostic(
                    "AndroidSdkDirectoryNotDeclared",
                    "The nearest local.properties file does not declare a valid sdk.dir value.",
                    localProperties));
                return null;
            }

            var parent = Path.GetDirectoryName(localProperties)!;
            return Path.GetFullPath(Path.IsPathRooted(sdkDirectory)
                ? sdkDirectory
                : Path.Combine(parent, sdkDirectory));
        }

        var home = ReadEnvironment("ANDROID_HOME");
        var root = ReadEnvironment("ANDROID_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(home)
            && !string.IsNullOrWhiteSpace(root)
            && !PathComparer.Equals(Path.GetFullPath(home), Path.GetFullPath(root)))
        {
            source = "ConflictingEnvironment";
            diagnostics.Add(Diagnostic(
                "AndroidSdkEnvironmentConflict",
                "ANDROID_HOME and ANDROID_SDK_ROOT identify different SDK roots. Configure one consistent installation or use explicit SDK configuration.",
                modulePath));
            return null;
        }

        var selected = FirstNonBlank(home, root);
        if (selected is null)
        {
            diagnostics.Add(Diagnostic(
                "AndroidSdkRootUnavailable",
                "No Android SDK root was found in explicit configuration, local.properties, ANDROID_HOME or ANDROID_SDK_ROOT.",
                modulePath));
            return null;
        }

        source = !string.IsNullOrWhiteSpace(home) ? "ANDROID_HOME" : "ANDROID_SDK_ROOT";
        return Path.GetFullPath(selected);
    }

    private static string? FindLocalProperties(string workspaceRoot, string modulePath)
    {
        var workspace = Path.GetFullPath(workspaceRoot);
        var current = Path.GetFullPath(modulePath);
        if (!IsSameOrDescendantPath(current, workspace))
            return null;

        while (IsSameOrDescendantPath(current, workspace))
        {
            var candidate = Path.Combine(current, "local.properties");
            if (File.Exists(candidate))
                return candidate;

            if (PathComparer.Equals(current, workspace))
                break;

            current = Path.GetDirectoryName(current)!;
        }

        return null;
    }

    private static bool TryReadSdkDirectory(string propertiesPath, out string sdkDirectory)
    {
        sdkDirectory = string.Empty;
        foreach (var rawLine in File.ReadLines(propertiesPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!'))
                continue;

            var match = Regex.Match(line, @"^sdk\.dir\s*[:=]\s*(.*?)\s*$", RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;

            sdkDirectory = match.Groups[1].Value
                .Replace(@"\\:", ":", StringComparison.Ordinal)
                .Replace(@"\\ ", " ", StringComparison.Ordinal)
                .Replace(@"\\\\", @"\", StringComparison.Ordinal);
            return !string.IsNullOrWhiteSpace(sdkDirectory);
        }

        return false;
    }

    private static string? ResolvePlatformName(
        GradleAndroidBuildModel android,
        ICollection<AnalysisDiagnostic> diagnostics)
    {
        var compileSdk = FirstNonBlank(android.CompileSdkPreview, android.CompileSdk);
        if (compileSdk is null)
        {
            diagnostics.Add(Diagnostic(
                "AndroidCompileSdkUnavailable",
                "The Android module does not declare a statically resolvable compileSdk or compileSdkPreview.",
                android.PluginId));
            return null;
        }

        var value = compileSdk.Trim();
        if (value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar)
            || value is "." or "..")
        {
            diagnostics.Add(Diagnostic(
                "AndroidCompileSdkInvalid",
                "The declared compile SDK value is not a valid platform identifier.",
                value));
            return null;
        }

        return value.StartsWith("android-", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"android-{value}";
    }

    private string? ReadEnvironment(string key) =>
        _environment.TryGetValue(key, out var value) ? NormalizeOptionalPath(value) : null;

    private static string? NormalizeOptionalPath(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value.Trim());

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static bool IsSameOrDescendantPath(string candidate, string parent)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return relative == "."
            || (!Path.IsPathRooted(relative)
                && relative != ".."
                && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static AnalysisDiagnostic Diagnostic(string code, string message, string target) => new()
    {
        Code = code,
        Component = nameof(AndroidSdkResolver),
        Message = message,
        Target = target,
        AffectsCoverage = true
    };

    private static AndroidSdkResolution Incomplete(
        string? sdkRoot,
        string? platformDirectory,
        string? androidJar,
        string? compileSdk,
        string source,
        IReadOnlyList<AnalysisDiagnostic> diagnostics) =>
        new(sdkRoot, platformDirectory, androidJar, compileSdk, source, false, diagnostics);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
