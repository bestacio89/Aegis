using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Architecture;

public sealed class ArchitecturalEvaluator :
    BaseArchitectureEvaluator,
    IScopedDependency
{
    private readonly ArchitecturePolicy _policy;


    public override string Name =>
        "ArchitecturalEvaluator";


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
        _policy =
            options.Value.Architecture
            ?? new ArchitecturePolicy();
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



        foreach (var dependency in Context.Dependencies)
        {
            token.ThrowIfCancellationRequested();


            if (!IsValidDependency(dependency))
                continue;


            var allowed =
                IsAllowedDependency(
                    dependency.SourceLayer,
                    dependency.TargetLayer);


            results.Add(
                CreateResult(
                    dependency,
                    allowed));
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Architecture dependency evaluation completed. Findings: {results.Count}");



        return Task.FromResult<IEnumerable<ArchitectureEvaluatorResult>>(results);
    }



    private bool IsValidDependency(
        ArchitectureDependencyContext dependency)
    {
        return
            !string.IsNullOrWhiteSpace(dependency.SourceLayer)
            &&
            !string.IsNullOrWhiteSpace(dependency.TargetLayer)
            &&
            !dependency.SourceLayer.Equals(
                dependency.TargetLayer,
                StringComparison.OrdinalIgnoreCase);
    }



    private bool IsAllowedDependency(
        string source,
        string target)
    {
        return
            _policy.AllowedDependencies.TryGetValue(
                source,
                out var targets)
            &&
            targets.Contains(
                target,
                StringComparer.OrdinalIgnoreCase);
    }



    private ArchitectureEvaluatorResult CreateResult(
        ArchitectureDependencyContext dependency,
        bool allowed)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            dependency.File)
        {
            Category = "Architecture",

            Metrics =
            {
                ["AllowedDependency"] =
                    allowed ? 1 : 0
            },

            Metadata =
            {
                ["Source"] = dependency.Source,
                ["Target"] = dependency.Target,

                ["SourceLayer"] =
                    dependency.SourceLayer,

                ["TargetLayer"] =
                    dependency.TargetLayer,

                ["DependencyType"] =
                    dependency.DependencyType,

                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown",

                ["Allowed"] =
                    allowed.ToString()
            }
        };
    }
}