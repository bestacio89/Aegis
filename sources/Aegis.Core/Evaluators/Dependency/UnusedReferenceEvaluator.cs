using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Dependency;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Text.Json;

namespace Aegis.Architecture.Evaluators.Dependency;


/// <summary>
/// Evaluates dependency manifest metadata discovered during project analysis.
///
/// This evaluator does not determine source usage.
/// It produces deterministic dependency facts consumed by the RuleEngine.
///
/// Responsibilities:
/// - Analyze declared dependency references
/// - Count manifest references
/// - Produce dependency metrics
///
/// Source usage analysis belongs to language-specific analyzers.
/// </summary>
public sealed class UnusedReferenceEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DependencyPolicy _policy;



    public override string Name =>
        "UnusedReferenceEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "TypeScript",
        "JavaScript",
        "Python"
    ];



    public override string[] SupportedFrameworks =>
    [
        "DotNet",
        "Node",
        "Python"
    ];



    public UnusedReferenceEvaluator(
        ILogger<UnusedReferenceEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Dependency
            ?? new DependencyPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        if (Context is null)
            return results;



        if (!_policy.Enabled ||
            !_policy.CheckUnusedReferences)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Unused reference evaluation disabled by policy.");

            return results;
        }



        var manifests =
            EnumerateApplicationFiles(projectPath)
                .Where(IsDependencyManifest)
                .ToList();



        if (manifests.Count == 0)
            return results;



        foreach (var manifest in manifests)
        {
            token.ThrowIfCancellationRequested();



            var metrics =
                await AnalyzeManifestAsync(
                    manifest,
                    token);



            if (metrics.Count == 0)
                continue;



            results.Add(
                CreateResult(
                    manifest,
                    metrics));
        }



        results.Add(
            CreateSummary(
                projectPath,
                results));



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Unused reference evaluation completed with {results.Count} entries.");



        return results;
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        Dictionary<string, double> metrics)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            ProjectName =
                Context?.ProjectName ?? string.Empty,

            Language =
                Context?.Language ?? "Unknown",

            Framework =
                Context?.Framework,

            DetectionConfidence =
                Context?.Confidence ?? 0,

            Category =
                nameof(ArchitectureRuleCategory.Dependency),


            Metrics =
                metrics,


            Metadata =
            {
                ["Manifest"] =
                    Path.GetFileName(file),

                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown"
            }
        };
    }



    private static async Task<Dictionary<string, double>>
        AnalyzeManifestAsync(
            string file,
            CancellationToken token)
    {
        var metrics =
            new Dictionary<string, double>();



        var extension =
            Path.GetExtension(file);



        var content =
            await File.ReadAllTextAsync(
                file,
                token);



        switch (extension.ToLowerInvariant())
        {
            case ".csproj":
                {
                    metrics["DeclaredReferences"] =
                        CountOccurrences(
                            content,
                            "<PackageReference");

                    break;
                }



            case ".json":
                {
                    try
                    {
                        using var json =
                            JsonDocument.Parse(content);


                        metrics["DeclaredReferences"] =
                            CountJsonDependencies(
                                json.RootElement);
                    }
                    catch
                    {
                        // Invalid manifests are ignored.
                    }

                    break;
                }



            case ".txt":
                {
                    metrics["DeclaredReferences"] =
                        content
                            .Split(
                                '\n',
                                StringSplitOptions.RemoveEmptyEntries)
                            .Count(
                                line =>
                                    !line
                                        .TrimStart()
                                        .StartsWith("#"));

                    break;
                }
        }



        return metrics;
    }



    private static int CountJsonDependencies(
        JsonElement root)
    {
        var count = 0;



        foreach (var propertyName in new[]
        {
            "dependencies",
            "devDependencies"
        })
        {
            if (!root.TryGetProperty(
                    propertyName,
                    out var dependencies))
            {
                continue;
            }



            count +=
                dependencies
                    .EnumerateObject()
                    .Count();
        }



        return count;
    }



    private static int CountOccurrences(
        string content,
        string value)
    {
        var count = 0;

        var index = 0;



        while ((index =
            content.IndexOf(
                value,
                index,
                StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;

            index += value.Length;
        }



        return count;
    }



    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        IEnumerable<ArchitectureEvaluatorResult> results)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            ProjectName =
                Context?.ProjectName ?? string.Empty,

            Language =
                Context?.Language ?? "Unknown",

            Framework =
                Context?.Framework,


            Category =
                "DependencySummary",


            Metrics =
            {
                ["ManifestCount"] =
                    results.Count(),

                ["DeclaredReferences"] =
                    results.Sum(
                        result =>
                            result.Metrics
                                .GetValueOrDefault(
                                    "DeclaredReferences"))
            },


            Metadata =
            {
                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown"
            }
        };
    }



    private static bool IsDependencyManifest(
        string file)
    {
        return
            file.EndsWith(
                ".csproj",
                StringComparison.OrdinalIgnoreCase)

            ||

            file.EndsWith(
                "package.json",
                StringComparison.OrdinalIgnoreCase)

            ||

            file.EndsWith(
                "requirements.txt",
                StringComparison.OrdinalIgnoreCase);
    }
}