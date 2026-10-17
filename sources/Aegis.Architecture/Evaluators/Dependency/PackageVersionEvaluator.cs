using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Dependency;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Dependency;

/// <summary>
/// Evaluates dependency version declarations discovered in supported manifests.
///
/// Supported manifests:
/// - packages.config
/// - package.json
/// - requirements.txt
///
/// This evaluator extracts declared dependency version constraints.
/// It does not query package registries or determine whether dependencies
/// are outdated, vulnerable, or compatible.
/// </summary>
public sealed class PackageVersionEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DependencyPolicy _policy;

    private static readonly Regex PythonRequirementRegex = new(
        @"^\s*(?<name>[A-Za-z0-9][A-Za-z0-9._-]*(?:\[[^\]]+\])?)\s*(?<specifier>(?:===|==|~=|!=|<=|>=|<|>).+?)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonVersionOperatorRegex = new(
        @"(?:===|==|~=|!=|<=|>=|<|>)\s*\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> PackageJsonDependencySections =
        new(StringComparer.Ordinal)
        {
            "dependencies",
            "devDependencies",
            "optionalDependencies",
            "peerDependencies"
        };

    public override string Name => "PackageVersionEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "JavaScript",
        "TypeScript",
        "Python"
    ];

    public override string[] SupportedFrameworks =>
    [
        ".NET",
        "Node",
        "Python"
    ];

    public PackageVersionEvaluator(
        ILogger<PackageVersionEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Dependency
            ?? new DependencyPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var results = new List<ArchitectureEvaluatorResult>();

        if (Context is not { } context)
        {
            return results;
        }

        if (!_policy.Enabled || !_policy.CheckOutdatedPackages)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Package version evaluation skipped because dependency policy disables this evaluator.");

            return results;
        }

        var files = ResolveSourceFiles(
                projectPath,
                ".json",
                ".txt",
                ".config")
            .Where(IsDependencyManifest)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "No supported dependency manifests were found.");

            return results;
        }

        var analyzedManifestCount = 0;
        var unreadableManifestCount = 0;
        var parseFailureCount = 0;
        var totalVersionDeclarations = 0;

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Analyzing {files.Count} dependency manifest(s).");

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
            {
                unreadableManifestCount++;

                AegisDiagnostics.Report(
                    Name,
                    DiagnosticLevel.Warning,
                    $"Unable to read dependency manifest '{file}'.",
                    ex);

                continue;
            }

            token.ThrowIfCancellationRequested();

            int declarationCount;
            string parseStatus;

            try
            {
                declarationCount = CountVersionDeclarations(file, content);
                parseStatus = "Success";
                analyzedManifestCount++;
            }
            catch (Exception ex) when (
                ex is JsonException
                or XmlException
                or InvalidOperationException
                or FormatException)
            {
                declarationCount = 0;
                parseStatus = "Failed";
                parseFailureCount++;

                AegisDiagnostics.Report(
                    Name,
                    DiagnosticLevel.Warning,
                    $"Unable to parse dependency manifest '{file}'.",
                    ex);
            }

            totalVersionDeclarations += declarationCount;

            results.Add(
                CreateManifestResult(
                    file,
                    declarationCount,
                    parseStatus));
        }

        token.ThrowIfCancellationRequested();

        results.Add(
            CreateSummary(
                analyzedManifestCount: analyzedManifestCount,
                discoveredManifestCount: files.Count,
                unreadableManifestCount: unreadableManifestCount,
                parseFailureCount: parseFailureCount,
                totalVersionDeclarations: totalVersionDeclarations));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Dependency version analysis completed. " +
            $"Discovered: {files.Count}; " +
            $"Analyzed: {analyzedManifestCount}; " +
            $"Unreadable: {unreadableManifestCount}; " +
            $"Parse failures: {parseFailureCount}; " +
            $"Version declarations: {totalVersionDeclarations}.");

        return results;
    }

    private static int CountVersionDeclarations(
        string file,
        string content)
    {
        var fileName = Path.GetFileName(file);

        if (fileName.Equals(
                "packages.config",
                StringComparison.OrdinalIgnoreCase))
        {
            return CountPackagesConfigDeclarations(content);
        }

        if (fileName.Equals(
                "package.json",
                StringComparison.OrdinalIgnoreCase))
        {
            return CountPackageJsonDeclarations(content);
        }

        if (fileName.Equals(
                "requirements.txt",
                StringComparison.OrdinalIgnoreCase))
        {
            return CountPythonRequirementDeclarations(content);
        }

        return 0;
    }

    private static int CountPackagesConfigDeclarations(string content)
    {
        var document = XDocument.Parse(content);

        return document
            .Descendants()
            .Where(element =>
                element.Name.LocalName.Equals(
                    "package",
                    StringComparison.OrdinalIgnoreCase))
            .Count(element =>
                !string.IsNullOrWhiteSpace(
                    (string?)element.Attribute("id"))
                && !string.IsNullOrWhiteSpace(
                    (string?)element.Attribute("version")));
    }

    private static int CountPackageJsonDeclarations(string content)
    {
        using var document = JsonDocument.Parse(content);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                "The package.json root must be a JSON object.");
        }

        var declarations = 0;

        foreach (var section in document.RootElement.EnumerateObject())
        {
            if (!PackageJsonDependencySections.Contains(section.Name))
            {
                continue;
            }

            if (section.Value.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException(
                    $"The '{section.Name}' section must be a JSON object.");
            }

            foreach (var dependency in section.Value.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(dependency.Name))
                {
                    continue;
                }

                if (dependency.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var versionSpec = dependency.Value.GetString();

                if (string.IsNullOrWhiteSpace(versionSpec))
                {
                    continue;
                }

                if (IsExternalOrWorkspaceReference(versionSpec))
                {
                    continue;
                }

                declarations++;
            }
        }

        return declarations;
    }

    private static bool IsExternalOrWorkspaceReference(string versionSpec)
    {
        var value = versionSpec.Trim();

        return value.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("git:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("git+", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("link:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("workspace:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("catalog:", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountPythonRequirementDeclarations(string content)
    {
        var declarations = 0;

        foreach (var originalLine in content.Split('\n'))
        {
            var line = originalLine.Trim();

            if (line.Length == 0
                || line.StartsWith('#')
                || line.StartsWith('-')
                || line.StartsWith("--"))
            {
                continue;
            }

            // Editable installs, direct URLs, local paths, and pip options
            // do not follow the ordinary name + version-specifier grammar.
            if (line.StartsWith(
                    "git+",
                    StringComparison.OrdinalIgnoreCase)
                || line.StartsWith(
                    "http://",
                    StringComparison.OrdinalIgnoreCase)
                || line.StartsWith(
                    "https://",
                    StringComparison.OrdinalIgnoreCase)
                || line.StartsWith(
                    "file:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Remove environment markers before parsing the requirement.
            var markerIndex = line.IndexOf(';');

            if (markerIndex >= 0)
            {
                line = line[..markerIndex].Trim();
            }

            // A direct URL requirement uses @ rather than a version
            // constraint, for example: package @ https://example.org/pkg.whl
            if (line.Contains('@'))
            {
                continue;
            }

            var match = PythonRequirementRegex.Match(line);

            if (!match.Success)
            {
                continue;
            }

            var specifier = match.Groups["specifier"].Value;

            if (string.IsNullOrWhiteSpace(specifier))
            {
                // A package name without a version constraint is a
                // dependency declaration, but not a version declaration.
                continue;
            }

            if (PythonVersionOperatorRegex.IsMatch(specifier))
            {
                declarations++;
            }
        }

        return declarations;
    }

    private ArchitectureEvaluatorResult CreateManifestResult(
        string file,
        int versionDeclarationCount,
        string parseStatus)
    {
        var context = Context;

        var result = CreateResult(file, "Dependency");

        result.ProjectName = context?.ProjectName;
        result.Language = context?.Language;
        result.Framework = context?.Framework;
        result.DetectionConfidence = context?.Confidence ?? 0;
        result.Category = nameof(ArchitectureRuleCategory.Dependency);

        result.Metrics["DeclaredVersionCount"] = versionDeclarationCount;
        result.Metrics["ManifestDetected"] = 1;

        result.Metadata["FilePath"] = file;
        result.Metadata["DependencyFile"] = Path.GetFileName(file);
        result.Metadata["ParseStatus"] = parseStatus;
        result.Metadata["Language"] = context?.Language ?? "Unknown";
        result.Metadata["Framework"] = context?.Framework ?? "Unknown";
        result.Metadata["MeasurementMethod"] =
            "Count of dependency entries with an identifiable version or version constraint.";

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        int analyzedManifestCount,
        int discoveredManifestCount,
        int unreadableManifestCount,
        int parseFailureCount,
        int totalVersionDeclarations)
    {
        var context = Context;

        var result = new ArchitectureEvaluatorResult(
            Name,
            "DependencyVersionSummary")
        {
            ProjectName = context?.ProjectName,
            Language = context?.Language,
            Framework = context?.Framework,
            DetectionConfidence = context?.Confidence ?? 0,
            Category = "DependencySummary"
        };

        result.Metrics["ManifestCount"] = discoveredManifestCount;
        result.Metrics["AnalyzedManifestCount"] = analyzedManifestCount;
        result.Metrics["UnreadableManifestCount"] = unreadableManifestCount;
        result.Metrics["ParseFailureCount"] = parseFailureCount;
        result.Metrics["VersionDeclarations"] = totalVersionDeclarations;

        result.Metadata["EvaluationScope"] = ProjectScope;
        result.Metadata["Language"] = context?.Language ?? "Unknown";
        result.Metadata["Framework"] = context?.Framework ?? "Unknown";
        result.Metadata["MeasurementMethod"] =
            "Aggregate count of identifiable dependency version declarations in supported manifests.";
        result.Metadata["OutdatedPackagesChecked"] = false.ToString();
        result.Metadata["RegistryComparisonPerformed"] = false.ToString();

        return result;
    }

    private static bool IsDependencyManifest(string file)
    {
        var fileName = Path.GetFileName(file);

        return fileName.Equals(
                   "packages.config",
                   StringComparison.OrdinalIgnoreCase)
               || fileName.Equals(
                   "package.json",
                   StringComparison.OrdinalIgnoreCase)
               || fileName.Equals(
                   "requirements.txt",
                   StringComparison.OrdinalIgnoreCase);
    }
}