using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.BackEnd;

public sealed class CouplingEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly CouplingPolicy _policy;



    public override string Name =>
        "CouplingEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "TypeScript",
        "JavaScript"
    ];



    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring Boot",
        "FastAPI",
        "Angular",
        "React",
        "Node"
    ];



    public CouplingEvaluator(
        ILogger<CouplingEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Coupling
            ?? new CouplingPolicy();
    }



    protected override Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        if (Context is null)
        {
            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
                results);
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            "Evaluating coupling against detected architecture boundaries.");



        EvaluateArchitecturalCoupling(
            results,
            token);



        EvaluateExternalCoupling(
            results);



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Coupling evaluation completed with {results.Count} violation(s).");



        return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            results);
    }



    private void EvaluateArchitecturalCoupling(
        List<ArchitectureEvaluatorResult> results,
        CancellationToken token)
    {
        if (Context is null)
            return;



        foreach (var dependency in Context.Dependencies)
        {
            token.ThrowIfCancellationRequested();



            if (!IsArchitecturalViolation(dependency))
                continue;



            results.Add(
                CreateArchitecturalViolation(
                    dependency));
        }
    }



    private bool IsArchitecturalViolation(
        ArchitectureDependencyContext dependency)
    {
        if (Context is null)
            return false;



        if (string.IsNullOrWhiteSpace(
                dependency.SourceLayer)
            ||
            string.IsNullOrWhiteSpace(
                dependency.TargetLayer))
        {
            return false;
        }



        if (dependency.SourceLayer.Equals(
                dependency.TargetLayer,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }



        var boundary =
            Context.Boundaries
                .FirstOrDefault(
                    x =>
                        x.SourceLayer.Equals(
                            dependency.SourceLayer,
                            StringComparison.OrdinalIgnoreCase));



        if (boundary is null)
            return false;



        if (boundary.ForbiddenDependencies.Contains(
                dependency.TargetLayer,
                StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }



        if (boundary.AllowedDependencies.Count > 0)
        {
            return !boundary.AllowedDependencies.Contains(
                dependency.TargetLayer,
                StringComparer.OrdinalIgnoreCase);
        }



        return false;
    }



    private ArchitectureEvaluatorResult CreateArchitecturalViolation(
        ArchitectureDependencyContext dependency)
    {
        var result =
            CreateResult(
                dependency.File,
                "ArchitecturalCouplingViolation");


        result.ProjectName =
            Context?.ProjectName;

        result.Language =
            Context?.Language;

        result.Framework =
            Context?.Framework;

        result.Layer =
            dependency.SourceLayer;

        result.DetectionConfidence =
            Context?.Confidence ?? 0;



        result.Metrics["InternalDependency"] =
            1;

        result.Metrics["Violation"] =
            1;



        result.Metadata["Rule"] =
            "CPL001";

        result.Metadata["Source"] =
            dependency.Source;

        result.Metadata["SourceLayer"] =
            dependency.SourceLayer;

        result.Metadata["Target"] =
            dependency.Target;

        result.Metadata["TargetLayer"] =
            dependency.TargetLayer;

        result.Metadata["DependencyType"] =
            dependency.DependencyType;



        return result;
    }



    private void EvaluateExternalCoupling(
        List<ArchitectureEvaluatorResult> results)
    {
        if (Context is null)
            return;



        var dependencyCount =
            Context.DetectedDependencies.Count;



        if (dependencyCount <=
            _policy.MaxExternalDependencies)
        {
            return;
        }



        var result =
            CreateResult(
                Context.RootPath,
                "ExternalCouplingViolation");



        result.ProjectName =
            Context.ProjectName;

        result.Language =
            Context.Language;

        result.Framework =
            Context.Framework;



        result.Metrics["ExternalDependencyCount"] =
            dependencyCount;

        result.Metrics["MaxExternalDependencies"] =
            _policy.MaxExternalDependencies;

        result.Metrics["Violation"] =
            1;



        result.Metadata["Rule"] =
            "CPL002";

        result.Metadata["Language"] =
            Context.Language;

        result.Metadata["Framework"] =
            Context.Framework
            ?? "Unknown";



        results.Add(result);
    }
}