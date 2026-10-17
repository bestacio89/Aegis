using System.Text.Json;
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
/// Evaluates dependency references declared in supported dependency manifests.
///
/// This evaluator measures declarations, not actual source-code usage.
/// It cannot determine whether a dependency is unused without language-specific
/// source analysis.
///
/// Supported manifests:
/// - .csproj
/// - package.json
/// - requirements.txt
///
/// Registry lookups, vulnerability checks, and source-usage analysis belong
/// to dedicated dependency intelligence services.
/// </summary>
public sealed class UnusedReferenceEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DependencyPolicy _policy;

    private static readonly HashSet<string> PackageJsonDependencySections =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "dependencies",
            "devDependencies",
            "optionalDependencies",
            "peerDependencies"
        };

    public override string Name => "UnusedReferenceEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "TypeScript",
        "JavaScript",
        "Python"
    ];

    public override string[] SupportedFrameworks =>
    [
        ".NET",
        "Node",
        "Python"
    ];

    public UnusedReferenceEvaluator(
        ILogger<UnusedReferenceEvaluator> logger,
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

        if (!_policy.Enabled || !_policy.CheckUnusedReferences)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Dependency reference analysis skipped because dependency policy disables this evaluator.");

            return results;
        }

        var manifests = ResolveSourceFiles(
                projectPath,
                ".csproj",
                ".json",
                ".txt")
            .Where(IsDependencyManifest)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (manifests.Count == 0)
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
        var totalDeclaredReferences = 0;

        foreach (var manifest in manifests)
        {
            token.ThrowIfCancellationRequested();

            ManifestAnalysis analysis;

            try
            {
                analysis = await AnalyzeManifestAsync(manifest, token);
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
                    $"Unable to read dependency manifest '{manifest}'.",
                    ex);

                results.Add(
                    CreateManifestResult(
                        manifest,
                        declaredReferenceCount: 0,
                        status: "Unreadable"));

                continue;
            }
            catch (Exception ex) when (
                ex is XmlException
                or JsonException
                or InvalidDataException
                or FormatException)
            {
                parseFailureCount++;

                AegisDiagnostics.Report(
                    Name,
                    DiagnosticLevel.Warning,
                    $"Unable to parse dependency manifest '{manifest}'.",
                    ex);

                results.Add(
                    CreateManifestResult(
                        manifest,
                        declaredReferenceCount: 0,
                        status: "ParseFailed"));

                continue;
            }

            analyzedManifestCount++;
            totalDeclaredReferences += analysis.DeclaredReferenceCount;

            results.Add(
                CreateManifestResult(
                    manifest,
                    analysis.DeclaredReferenceCount,
                    "Success"));
        }

        token.ThrowIfCancellationRequested();

        results.Add(
            CreateSummary(
                discoveredManifestCount: manifests.Count,
                analyzedManifestCount: analyzedManifestCount,
                unreadableManifestCount: unreadableManifestCount,
                parseFailureCount: parseFailureCount,
                totalDeclaredReferences: totalDeclaredReferences));

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Dependency declaration analysis completed. " +
            $"Discovered: {manifests.Count}; " +
            $"Analyzed: {analyzedManifestCount}; " +
            $"Unreadable: {unreadableManifestCount}; " +
            $"Parse failures: {parseFailureCount}; " +
            $"Declared references: {totalDeclaredReferences}.");

        return results;
    }

    private static async Task<ManifestAnalysis> AnalyzeManifestAsync(
        string file,
        CancellationToken token)
    {
        var content = await File.ReadAllTextAsync(file, token);

        token.ThrowIfCancellationRequested();

        var fileName = Path.GetFileName(file);

        if (fileName.Equals(
                "packages.config",
                StringComparison.OrdinalIgnoreCase))
        {
            // Kept as an explicit guard in case the resolver later expands
            // the supported manifest set.
            var document = XDocument.Parse(content);

            var packageCount = document
                .Descendants()
                .Where(element => element.Name.LocalName.Equals(
                    "package",
                    StringComparison.OrdinalIgnoreCase))
                .Count(element =>
                    !string.IsNullOrWhiteSpace(
                        (string?)element.Attribute("id")));

            return new ManifestAnalysis(packageCount);
        }

        if (fileName.EndsWith(
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ManifestAnalysis(
                CountProjectPackageReferences(content));
        }

        if (fileName.Equals(
                "package.json",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ManifestAnalysis(
                CountPackageJsonDependencies(content));
        }

        if (fileName.Equals(
                "requirements.txt",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ManifestAnalysis(
                CountPythonRequirements(content));
        }

        return new ManifestAnalysis(0);
    }

    private static int CountProjectPackageReferences(string content)
    {
        var document = XDocument.Parse(content);
        var count = 0;

        foreach (var element in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals(
                         "PackageReference",
                         StringComparison.OrdinalIgnoreCase)))
        {
            var include = (string?)element.Attribute("Include")
                          ?? (string?)element.Attribute("Update");

            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            // MSBuild permits multiple item values separated by semicolons.
            count += include
                .Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries)
                .Count(value => !string.IsNullOrWhiteSpace(value));
        }

        return count;
    }

    private static int CountPackageJsonDependencies(string content)
    {
        using var document = JsonDocument.Parse(content);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                "The package.json root must be a JSON object.");
        }

        var count = 0;

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

                // Dependency specifications are expected to be strings.
                // Non-string entries are not counted as valid declarations.
                if (dependency.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var specification = dependency.Value.GetString();

                if (string.IsNullOrWhiteSpace(specification))
                {
                    continue;
                }

                count++;
            }
        }

        return count;
    }

    private static int CountPythonRequirements(string content)
    {
        var count = 0;
        var continuation = new System.Text.StringBuilder();

        foreach (var originalLine in content.Split('\n'))
        {
            var line = originalLine.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            // Join backslash-continued requirement lines before processing.
            var continues = line.EndsWith('\\');
            if (continues)
            {
                line = line[..^1].TrimEnd();
            }

            continuation.Append(' ').Append(line);

            if (continues)
            {
                continue;
            }

            var requirement = continuation.ToString().Trim();
            continuation.Clear();

            if (IsPythonRequirementDeclaration(requirement))
            {
                count++;
            }
        }

        // Handle a final unterminated continuation conservatively.
        var remaining = continuation.ToString().Trim();

        if (remaining.Length > 0
            && IsPythonRequirementDeclaration(remaining))
        {
            count++;
        }

        return count;
    }

    private static bool IsPythonRequirementDeclaration(string line)
    {
        if (line.Length == 0 || line.StartsWith('#'))
        {
            return false;
        }

        // These are pip directives or file inclusions, not package entries.
        if (line.StartsWith('-'))
        {
            return false;
        }

        // Remove inline comments only when the # is preceded by whitespace,
        // avoiding accidental truncation of URL fragments.
        for (var index = 1; index < line.Length; index++)
        {
            if (line[index] == '#'
                && char.IsWhiteSpace(line[index - 1]))
            {
                line = line[..index].TrimEnd();
                break;
            }
        }

        if (line.Length == 0)
        {
            return false;
        }

        // Direct URL references still declare a dependency, even if they
        // do not use a conventional package version specifier.
        if (line.Contains(" @ ", StringComparison.Ordinal))
        {
            return true;
        }

        // Ordinary named requirements, including extras and version ranges.
        // This intentionally does not claim complete PEP 508 parsing.
        var endOfName = line.IndexOfAny(['=', '<', '>', '!', '~', ';', ' ']);

        var name = endOfName < 0
            ? line
            : line[..endOfName];

        if (name.EndsWith(']'))
        {
            var extrasStart = name.IndexOf('[');

            if (extrasStart >= 0)
            {
                name = name[..extrasStart];
            }
        }

        return name.Length > 0
            && name.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '_' or '-');
    }

    private ArchitectureEvaluatorResult CreateManifestResult(
        string file,
        int declaredReferenceCount,
        string status)
    {
        var context = Context;

        var result = CreateResult(
            file,
            "Dependency");

        result.ProjectName = context?.ProjectName ?? string.Empty;
        result.Language = context?.Language ?? "Unknown";
        result.Framework = context?.Framework;
        result.DetectionConfidence = context?.Confidence ?? 0;
        result.Category = nameof(ArchitectureRuleCategory.Dependency);

        result.Metrics["DeclaredReferences"] = declaredReferenceCount;
        result.Metrics["ManifestDetected"] = 1;

        result.Metadata["FilePath"] = file;
        result.Metadata["Manifest"] = Path.GetFileName(file);
        result.Metadata["ParseStatus"] = status;
        result.Metadata["Language"] = context?.Language ?? "Unknown";
        result.Metadata["Framework"] = context?.Framework ?? "Unknown";
        result.Metadata["MeasurementMethod"] =
            "Count of declared dependency entries in the supported manifest format.";
        result.Metadata["UnusedReferencesDetermined"] = false.ToString();

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        int discoveredManifestCount,
        int analyzedManifestCount,
        int unreadableManifestCount,
        int parseFailureCount,
        int totalDeclaredReferences)
    {
        var context = Context;

        var result = new ArchitectureEvaluatorResult(
            Name,
            "DependencyReferenceSummary")
        {
            ProjectName = context?.ProjectName ?? string.Empty,
            Language = context?.Language ?? "Unknown",
            Framework = context?.Framework,
            DetectionConfidence = context?.Confidence ?? 0,
            Category = "DependencySummary"
        };

        result.Metrics["ManifestCount"] = discoveredManifestCount;
        result.Metrics["AnalyzedManifestCount"] = analyzedManifestCount;
        result.Metrics["UnreadableManifestCount"] = unreadableManifestCount;
        result.Metrics["ParseFailureCount"] = parseFailureCount;
        result.Metrics["DeclaredReferences"] = totalDeclaredReferences;

        result.Metadata["EvaluationScope"] = ProjectScope;
        result.Metadata["Language"] = context?.Language ?? "Unknown";
        result.Metadata["Framework"] = context?.Framework ?? "Unknown";
        result.Metadata["MeasurementMethod"] =
            "Aggregate count of declared dependency entries in supported manifests.";
        result.Metadata["UnusedReferencesDetermined"] = false.ToString();

        return result;
    }

    private static bool IsDependencyManifest(string file)
    {
        var fileName = Path.GetFileName(file);

        return fileName.EndsWith(
                   ".csproj",
                   StringComparison.OrdinalIgnoreCase)
               || fileName.Equals(
                   "package.json",
                   StringComparison.OrdinalIgnoreCase)
               || fileName.Equals(
                   "requirements.txt",
                   StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ManifestAnalysis(int DeclaredReferenceCount);
}