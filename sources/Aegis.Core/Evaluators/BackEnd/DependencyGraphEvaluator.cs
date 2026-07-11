using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.BackEnd;


public sealed class DependencyGraphEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DependencyGraphPolicy _policy;



    public override string Name =>
        "DependencyGraphEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript",
        "JavaScript"
    ];



    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring Boot",
        "Angular",
        "React",
        "Node"
    ];



    public DependencyGraphEvaluator(
        ILogger<DependencyGraphEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.DependencyGraph
            ?? new DependencyGraphPolicy();
    }



    protected override Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        if (Context is null)
            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(results);



        if (Context.Dependencies.Count == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "No dependency relations available.");

            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
                results);
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Evaluating dependency graph with {Context.Dependencies.Count} relations.");



        EvaluateLayerCoupling(
            results,
            token);



        EvaluateGraphSummary(
            results);



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Dependency graph evaluation completed with {results.Count} result(s).");



        return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            results);
    }



    private void EvaluateLayerCoupling(
        List<ArchitectureEvaluatorResult> results,
        CancellationToken token)
    {
        if (Context is null)
            return;



        var groups =
            Context.Dependencies
                .GroupBy(
                    x => x.SourceLayer,
                    StringComparer.OrdinalIgnoreCase);



        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();



            var dependencies =
                group
                    .Select(x => x.TargetLayer)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();



            var dependencyCount =
                dependencies.Count;



            if (dependencyCount <=
                _policy.MaxDependenciesPerModule)
            {
                continue;
            }



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    group.Key)
                {
                    ProjectName =
                        Context.ProjectName,

                    Language =
                        Context.Language,

                    Framework =
                        Context.Framework,

                    Layer =
                        group.Key,

                    DetectionConfidence =
                        Context.Confidence,


                    Category =
                        "DependencyGraphViolation",


                    Metrics =
                    {
                        ["DependencyCount"] =
                            dependencyCount,

                        ["RelationCount"] =
                            group.Count(),

                        ["MaxDependenciesThreshold"] =
                            _policy.MaxDependenciesPerModule,

                        ["Violation"] =
                            1
                    },


                    Metadata =
                    {
                        ["Rule"] =
                            "DEP001",

                        ["Layer"] =
                            group.Key,

                        ["Dependencies"] =
                            string.Join(
                                ", ",
                                dependencies.Take(10))
                    }
                });
        }
    }



    private void EvaluateGraphSummary(
        List<ArchitectureEvaluatorResult> results)
    {
        if (Context is null)
            return;



        results.Add(
            new ArchitectureEvaluatorResult(
                Name,
                Context.RootPath)
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
                    "DependencyGraphSummary",


                Metrics =
                {
                    ["TotalRelations"] =
                        Context.Dependencies.Count,

                    ["TotalLayers"] =
                        Context.Dependencies
                            .Select(x => x.SourceLayer)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count(),

                    ["AverageDependencies"] =
                        CalculateAverageDependencies()
                },


                Metadata =
                {
                    ["Language"] =
                        Context.Language,

                    ["Framework"] =
                        Context.Framework
                        ?? "Unknown"
                }
            });
    }



    private double CalculateAverageDependencies()
    {
        if (Context is null)
            return 0;



        return Context.Dependencies
            .GroupBy(
                x => x.SourceLayer,
                StringComparer.OrdinalIgnoreCase)
            .Select(
                group =>
                    group
                        .Select(x => x.TargetLayer)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count())
            .DefaultIfEmpty(0)
            .Average();
    }
}