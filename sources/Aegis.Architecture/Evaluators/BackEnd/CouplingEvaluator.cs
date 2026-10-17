
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

    public override string Name => "CouplingEvaluator";

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
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Coupling
            ?? new CouplingPolicy();
    }

    protected override Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var results = new List<ArchitectureEvaluatorResult>();

        if (Context is not { } context)
        {
            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
                results);
        }

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            "Evaluating architectural and external dependency coupling.");

        var architecturalDependencies = context.Dependencies.ToList();

        var forbiddenCount = 0;
        var allowedCount = 0;
        var unclassifiedCount = 0;
        var crossLayerCount = 0;

        foreach (var dependency in architecturalDependencies)
        {
            token.ThrowIfCancellationRequested();

            var decision = ClassifyArchitecturalDependency(dependency);

            if (decision == DependencyDecision.SameLayer)
            {
                continue;
            }

            if (decision == DependencyDecision.Unclassified)
            {
                unclassifiedCount++;
                continue;
            }

            crossLayerCount++;

            if (decision == DependencyDecision.Forbidden)
            {
                forbiddenCount++;

                results.Add(
                    CreateArchitecturalViolation(dependency));
            }
            else
            {
                allowedCount++;
            }
        }

        token.ThrowIfCancellationRequested();

        AddArchitecturalSummary(
            results,
            crossLayerCount,
            allowedCount,
            forbiddenCount,
            unclassifiedCount);

        EvaluateExternalCoupling(results, token);

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Coupling evaluation completed: " +
            $"{forbiddenCount} forbidden, " +
            $"{unclassifiedCount} unclassified cross-layer dependencies.");

        return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            results);
    }

    private DependencyDecision ClassifyArchitecturalDependency(
        ArchitectureDependencyContext dependency)
    {
        if (string.IsNullOrWhiteSpace(dependency.SourceLayer)
            || string.IsNullOrWhiteSpace(dependency.TargetLayer))
        {
            return DependencyDecision.Unclassified;
        }

        if (dependency.SourceLayer.Equals(
                dependency.TargetLayer,
                StringComparison.OrdinalIgnoreCase))
        {
            return DependencyDecision.SameLayer;
        }

        var boundary = Context?.Boundaries.FirstOrDefault(
            candidate =>
                !string.IsNullOrWhiteSpace(candidate.SourceLayer)
                && candidate.SourceLayer.Equals(
                    dependency.SourceLayer,
                    StringComparison.OrdinalIgnoreCase));

        if (boundary is null)
        {
            return DependencyDecision.Unclassified;
        }

        if (boundary.ForbiddenDependencies.Contains(
                dependency.TargetLayer,
                StringComparer.OrdinalIgnoreCase))
        {
            return DependencyDecision.Forbidden;
        }

        if (boundary.AllowedDependencies.Count > 0)
        {
            return boundary.AllowedDependencies.Contains(
                dependency.TargetLayer,
                StringComparer.OrdinalIgnoreCase)
                ? DependencyDecision.Allowed
                : DependencyDecision.Forbidden;
        }

        // An existing boundary without explicit allow/deny rules
        // does not provide enough evidence to classify this dependency.
        return DependencyDecision.Unclassified;
    }

    private ArchitectureEvaluatorResult CreateArchitecturalViolation(
        ArchitectureDependencyContext dependency)
    {
        var result = CreateResult(
            dependency.File,
            "ArchitecturalCouplingViolation");

        result.ProjectName = Context?.ProjectName;
        result.Language = Context?.Language;
        result.Framework = Context?.Framework;
        result.Layer = dependency.SourceLayer;
        result.DetectionConfidence = Context?.Confidence ?? 0;

        result.Metrics["InternalDependency"] = 1;
        result.Metrics["Violation"] = 1;

        result.Metadata["Rule"] = "CPL001";
        result.Metadata["Source"] = dependency.Source;
        result.Metadata["SourceLayer"] = dependency.SourceLayer;
        result.Metadata["Target"] = dependency.Target;
        result.Metadata["TargetLayer"] = dependency.TargetLayer;
        result.Metadata["DependencyType"] = dependency.DependencyType;
        result.Metadata["EvaluationScope"] = FileScope;
        result.Metadata["PolicyClassification"] = "Forbidden";

        return result;
    }

    private void AddArchitecturalSummary(
        List<ArchitectureEvaluatorResult> results,
        int crossLayerCount,
        int allowedCount,
        int forbiddenCount,
        int unclassifiedCount)
    {
        var result = CreateProjectResult(
            "ArchitecturalCouplingSummary");

        result.Metrics["CrossLayerDependencyCount"] = crossLayerCount;
        result.Metrics["AllowedDependencyCount"] = allowedCount;
        result.Metrics["ForbiddenDependencyCount"] = forbiddenCount;
        result.Metrics["UnclassifiedDependencyCount"] = unclassifiedCount;

        // Compliance is only meaningful when every cross-layer dependency
        // has been classified. Do not report unknown dependencies as compliant.
        var fullyClassified =
            unclassifiedCount == 0;

        result.Metrics["ArchitecturalCouplingClassificationComplete"] =
            fullyClassified ? 1 : 0;

        if (fullyClassified && crossLayerCount > 0)
        {
            result.Metrics["ArchitecturalCouplingCompliance"] =
                allowedCount * 100.0 / crossLayerCount;
        }

        result.Metrics["Violation"] =
            forbiddenCount > 0 ? 1 : 0;

        result.Metadata["MeasurementMethod"] =
            "Cross-layer dependencies classified against detected architecture boundaries.";

        result.Metadata["ComplianceDefined"] =
            result.Metadata["ComplianceDefined"] =
            (fullyClassified && crossLayerCount > 0).ToString(); 

        results.Add(result);
    }

    private void EvaluateExternalCoupling(
        List<ArchitectureEvaluatorResult> results,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (Context is not { } context)
        {
            return;
        }

        // This assumes DetectedDependencies represents the external
        // dependencies detected by the architecture analysis pipeline.
        var dependencyCount = context.DetectedDependencies.Count;
        var configuredMaximum = _policy.MaxExternalDependencies;

        var exceeded = dependencyCount > configuredMaximum;

        var result = CreateProjectResult(
            "ExternalCouplingSummary");

        result.Metrics["ExternalDependencyCount"] = dependencyCount;
        result.Metrics["MaxExternalDependencies"] = configuredMaximum;
        result.Metrics["ExternalCouplingThresholdExceeded"] =
            exceeded ? 1 : 0;
        result.Metrics["Violation"] = exceeded ? 1 : 0;

        result.Metadata["Rule"] = "CPL002";
        result.Metadata["MeasurementMethod"] =
            "Count of dependencies supplied by ProjectArchitectureContext.DetectedDependencies.";
        result.Metadata["ThresholdExceeded"] = exceeded.ToString().ToLowerInvariant();

        if (exceeded)
        {
            results.Add(result);
            return;
        }

        // Keep the measurement even when the threshold is not exceeded.
        results.Add(result);
    }

    private ArchitectureEvaluatorResult CreateProjectResult(
        string resultName)
    {
        var context = Context;

        var result = new ArchitectureEvaluatorResult(Name, resultName)
        {
            ProjectName = context?.ProjectName,
            Language = context?.Language,
            Framework = context?.Framework,
            DetectionConfidence = context?.Confidence ?? 0
        };

        result.Metadata["EvaluationScope"] = ProjectScope;

        return result;
    }

    private enum DependencyDecision
    {
        SameLayer,
        Allowed,
        Forbidden,
        Unclassified
    }
}
