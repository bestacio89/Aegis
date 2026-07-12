using System.Text.RegularExpressions;

using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;


/// <summary>
/// Evaluates Facade pattern characteristics.
///
/// Produces structural facts:
/// - dependency surface
/// - orchestration complexity
/// - public API exposure
///
/// Rule interpretation belongs to RuleEngine.
/// </summary>
public sealed class FacadePatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;



    public override string Name =>
        "FacadePatternEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript"
    ];



    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring",
        "NestJS",
        "CleanArchitecture",
        "DDD"
    ];



    private static readonly string[] Extensions =
    [
        ".cs",
        ".java",
        ".ts"
    ];



    private static readonly Regex PublicMethodRx =
        new(
            @"public\s+\w+\s+\w+\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex DependencyFieldRx =
        new(
            @"\b(private|protected)\s+\w+\s+\w*(Service|Repository|Client)\b",
            RegexOptions.Compiled);



    private static readonly Regex DirectCallRx =
        new(
            @"\w*(Service|Repository|Client)\.\w+\s*\(",
            RegexOptions.Compiled);



    public FacadePatternEvaluator(
        ILogger<FacadePatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        if (!_policy.EnforceFacadePattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Facade pattern evaluation disabled.");

            return results;
        }



        if (Context is null)
            return results;



        var files =
            ResolveSourceFiles(
                projectPath,
                Extensions);



        if (files.Count == 0)
            return results;



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();



            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



            var publicMethods =
                PublicMethodRx.Matches(content)
                .Count;



            var dependencies =
                DependencyFieldRx.Matches(content)
                .Count;



            var serviceCalls =
                DirectCallRx.Matches(content)
                .Count;



            var dependencyRatio =
                CalculateRatio(
                    dependencies,
                    _policy.MaxDependenciesPerFacade);



            var surfaceRatio =
                CalculateRatio(
                    publicMethods,
                    _policy.MaxPublicMethodsPerFacade);



            var orchestrationRatio =
                CalculateRatio(
                    serviceCalls,
                    _policy.MaxServiceCallsPerFacade);



            results.Add(
                CreateResult(
                    file,
                    new()
                    {
                        ["DependencyCount"] =
                            dependencies,

                        ["PublicMethodCount"] =
                            publicMethods,

                        ["ServiceCallCount"] =
                            serviceCalls,

                        ["DependencyRatio"] =
                            dependencyRatio,

                        ["PublicSurfaceRatio"] =
                            surfaceRatio,

                        ["OrchestrationRatio"] =
                            orchestrationRatio
                    }));
        }



        if (results.Count > 0)
        {
            results.Add(
                CreateSummary(
                    projectPath,
                    results));
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Facade evaluation completed with {results.Count} entries.");



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

            Layer =
                ResolveLayer(file),

            DetectionConfidence =
                Context?.Confidence ?? 0,


            Category =
                "DesignPattern",


            Metrics =
                metrics,


            Metadata =
            {
                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown"
            }
        };
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

            Category =
                "DesignPatternSummary",

            Metrics =
            {
                ["FacadeCount"] =
                    results.Count(),

                ["AverageDependencyCount"] =
                    results.Average(
                        x =>
                            x.Metrics.GetValueOrDefault(
                                "DependencyCount"))
            },

            Metadata =
            {
                ["Evaluator"] =
                    Name
            }
        };
    }



    private string ResolveLayer(
        string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(
                layer =>
                    layer.Files.Contains(
                        file,
                        StringComparer.OrdinalIgnoreCase))
            ?.Name
            ??
            "Unknown";
    }



    private static double CalculateRatio(
        int value,
        int maximum)
    {
        return value /
            (double)Math.Max(
                1,
                maximum);
    }
}