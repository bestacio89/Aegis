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



            var result =
                CreateResult(
                    group.Key,
                    "DependencyGraphViolation");



            result.ProjectName =
                Context.ProjectName;

            result.Language =
                Context.Language;

            result.Framework =
                Context.Framework;

            result.Layer =
                group.Key;

            result.DetectionConfidence =
                Context.Confidence;



            result.Metrics["DependencyCount"] =
                dependencyCount;

            result.Metrics["RelationCount"] =
                group.Count();

            result.Metrics["MaxDependenciesThreshold"] =
                _policy.MaxDependenciesPerModule;

            result.Metrics["Violation"] =
                1;



            result.Metadata["Rule"] =
                "DEP001";

            result.Metadata["Layer"] =
                group.Key;

            result.Metadata["Dependencies"] =
                string.Join(
                    ", ",
                    dependencies.Take(10));



            results.Add(result);
        }
    }



    private void EvaluateGraphSummary(
        List<ArchitectureEvaluatorResult> results)
    {
        if (Context is null)
            return;



        var result =
            CreateResult(
                Context.RootPath,
                "DependencyGraphSummary");



        result.ProjectName =
            Context.ProjectName;

        result.Language =
            Context.Language;

        result.Framework =
            Context.Framework;

        result.DetectionConfidence =
            Context.Confidence;



        result.Metrics["TotalRelations"] =
            Context.Dependencies.Count;

        result.Metrics["TotalLayers"] =
            Context.Dependencies
                .Select(x => x.SourceLayer)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

        result.Metrics["AverageDependencies"] =
            CalculateAverageDependencies();



        result.Metadata["Language"] =
            Context.Language;

        result.Metadata["Framework"] =
            Context.Framework
            ?? "Unknown";



        results.Add(result);
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