
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Architecture;

/// <summary>
/// Evaluates dependencies between architectural layers.
///
/// This evaluator produces dependency-level evidence and, when the policy
/// classification is complete, project-level aggregate metrics.
/// Rule interpretation and compliance decisions remain the responsibility
/// of RuleEngineCore.
/// </summary>
public sealed class ArchitecturalEvaluator :
    BaseArchitectureEvaluator,
    IScopedDependency
{
    private readonly ArchitecturePolicy _policy;

    public override string Name => "ArchitecturalEvaluator";

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
        "Node",
        "Angular",
        "React",
        "Vue"
    ];

    public ArchitecturalEvaluator(
        ILogger<ArchitecturalEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Architecture
            ?? new ArchitecturePolicy();
    }

    protected override Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (Context is null)
        {
            return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
                []);
        }

        var dependencies = Context.Dependencies.ToList();
        var results = new List<ArchitectureEvaluatorResult>();

        var classifiedCount = 0;
        var allowedCount = 0;
        var forbiddenCount = 0;
        var incompleteCount = 0;

        foreach (var dependency in dependencies)
        {
            token.ThrowIfCancellationRequested();

            if (!HasRequiredDependencyData(dependency))
            {
                incompleteCount++;

                AegisDiagnostics.Report(
                    Name,
                    DiagnosticLevel.Warning,
                    "Skipped dependency with incomplete source, target, or layer data.");

                continue;
            }

            var decision = ClassifyDependency(
                dependency.SourceLayer,
                dependency.TargetLayer);

            var result = CreateDependencyResult(
                dependency,
                decision);

            results.Add(result);

            if (!decision.IsClassified)
            {
                incompleteCount++;
                continue;
            }

            classifiedCount++;

            if (decision.IsAllowed)
            {
                allowedCount++;
            }
            else
            {
                forbiddenCount++;
            }
        }

        // Aggregate metrics are emitted only when every supplied dependency
        // could be classified. Partial data must not masquerade as a complete
        // project-wide compliance measurement.
        if (dependencies.Count > 0
            && incompleteCount == 0
            && classifiedCount == dependencies.Count)
        {
            results.Add(
                CreateProjectSummary(
                    dependencies.Count,
                    allowedCount,
                    forbiddenCount));
        }

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Architecture dependency evaluation completed. " +
            $"Dependencies={dependencies.Count}; " +
            $"Classified={classifiedCount}; " +
            $"Allowed={allowedCount}; " +
            $"Forbidden={forbiddenCount}; " +
            $"UnclassifiedOrIncomplete={incompleteCount}.");

        return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(
            results);
    }

    private static bool HasRequiredDependencyData(
        ArchitectureDependencyContext dependency)
    {
        return
            !string.IsNullOrWhiteSpace(dependency.File)
            && !string.IsNullOrWhiteSpace(dependency.Source)
            && !string.IsNullOrWhiteSpace(dependency.Target)
            && !string.IsNullOrWhiteSpace(dependency.SourceLayer)
            && !string.IsNullOrWhiteSpace(dependency.TargetLayer);
    }

    private DependencyDecision ClassifyDependency(
        string sourceLayer,
        string targetLayer)
    {
        // Do not depend on the dictionary's configured key comparer.
        var policyEntry = _policy.AllowedDependencies
            .FirstOrDefault(entry =>
                string.Equals(
                    entry.Key,
                    sourceLayer,
                    StringComparison.OrdinalIgnoreCase));

        if (policyEntry.Key is null || policyEntry.Value is null)
        {
            return DependencyDecision.Unclassified(
                "No usable allowed-dependencies policy entry exists " +
                $"for source layer '{sourceLayer}'.");
        }

        var allowed = policyEntry.Value.Contains(
            targetLayer,
            StringComparer.OrdinalIgnoreCase);

        return allowed
            ? DependencyDecision.Allowed(
                $"Policy permits '{sourceLayer}' to depend on '{targetLayer}'.")
            : DependencyDecision.Forbidden(
                $"Policy does not permit '{sourceLayer}' to depend on '{targetLayer}'.");
    }

    private ArchitectureEvaluatorResult CreateDependencyResult(
        ArchitectureDependencyContext dependency,
        DependencyDecision decision)
    {
        var result = CreateResult(
            dependency.File,
            nameof(ArchitectureRuleCategory.Architecture));

        result.Metadata["Source"] = dependency.Source;
        result.Metadata["Target"] = dependency.Target;
        result.Metadata["SourceLayer"] = dependency.SourceLayer;
        result.Metadata["TargetLayer"] = dependency.TargetLayer;
        result.Metadata["DependencyType"] = dependency.DependencyType;
        result.Metadata["Language"] = Context?.Language ?? "Unknown";
        result.Metadata["Framework"] = Context?.Framework ?? "Unknown";
        result.Metadata["PolicyDecision"] = decision.Status;
        result.Metadata["PolicyEvidence"] = decision.Reason;

        if (!decision.IsClassified)
        {
            // Do not emit AllowedDependency=0 or DependencyViolation=1
            // for an unknown policy classification.
            result.Metrics["PolicyClassificationKnown"] = 0;

            return result;
        }

        result.Metrics["PolicyClassificationKnown"] = 1;
        result.Metrics["AllowedDependency"] = decision.IsAllowed ? 1 : 0;
        result.Metrics["DependencyViolation"] = decision.IsAllowed ? 0 : 1;

        return result;
    }

    private ArchitectureEvaluatorResult CreateProjectSummary(
        int totalDependencies,
        int allowedCount,
        int forbiddenCount)
    {
        var compliance = totalDependencies == 0
            ? 100.0
            : allowedCount * 100.0 / totalDependencies;

        var result = new ArchitectureEvaluatorResult(
            Name,
            "ArchitectureSummary")
        {
            Category = nameof(ArchitectureRuleCategory.Architecture),
            Metrics = new Dictionary<string, double>
            {
                ["TotalDependencyCount"] = totalDependencies,
                ["AllowedDependencyCount"] = allowedCount,
                ["ForbiddenReferenceCount"] = forbiddenCount,
                ["LayerIsolationCompliance"] = compliance
            },
            Metadata = new Dictionary<string, string>
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["MetricScope"] = ProjectScope,
                ["PolicyClassificationComplete"] = "true",
                ["Language"] = Context?.Language ?? "Unknown",
                ["Framework"] = Context?.Framework ?? "Unknown"
            }
        };

        return result;
    }

    private sealed record DependencyDecision(
        bool IsClassified,
        bool IsAllowed,
        string Status,
        string Reason)
    {
        public static DependencyDecision Allowed(string reason) =>
            new(true, true, "Allowed", reason);

        public static DependencyDecision Forbidden(string reason) =>
            new(true, false, "Forbidden", reason);

        public static DependencyDecision Unclassified(string reason) =>
            new(false, false, "Unclassified", reason);
    }
}
