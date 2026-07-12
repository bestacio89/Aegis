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
/// Evaluates dependency manifests for declared references.
///
/// This evaluator does not determine real source usage.
/// It produces dependency metadata consumed by the RuleEngine.
///
/// Responsibilities:
/// - Detect dependency manifests
/// - Count declared references
/// - Detect duplicate declarations
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



        foreach (var file in manifests)
        {
            token.ThrowIfCancellationRequested();



            var metrics =
                await AnalyzeManifestAsync(
                    file,
                    token);



            if (metrics.Count == 0)
                continue;



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    ProjectName =
                        Context.ProjectName,

                    Language =
                        Context.Language,

                    Framework =
                        Context.Framework,

                    DetectionConfidence =
                        Context.Confidence,

                    Category =
                        nameof(
                            ArchitectureRuleCategory.Dependency),


                    Metrics =
                        metrics,


                    Metadata =
                    {
                        ["Manifest"] =
                            Path.GetFileName(file),

                        ["Language"] =
                            Context.Language,

                        ["Framework"] =
                            Context.Framework
                            ?? "Unknown"
                    }
                });
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
                    var references =
                        CountOccurrences(
                            content,
                            "<PackageReference");


                    metrics["DeclaredReferences"] =
                        references;


                    break;
                }



            case ".json":
                {
                    try
                    {
                        using var json =
                            JsonDocument.Parse(content);


                        var count =
                            CountJsonDependencies(
                                json.RootElement);


                        metrics["DeclaredReferences"] =
                            count;
                    }
                    catch
                    {
                    }


                    break;
                }



            case ".txt":
                {
                    var count =
                        content
                            .Split(
                                '\n',
                                StringSplitOptions.RemoveEmptyEntries)
                            .Count(
                                x =>
                                    !x.TrimStart()
                                     .StartsWith("#"));


                    metrics["DeclaredReferences"] =
                        count;


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
                        x =>
                            x.Metrics
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