using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Evaluates Facade pattern compliance:
/// - Measures dependency coupling (services, repositories, clients)
/// - Evaluates public surface exposure
/// - Detects orchestration complexity and excessive service calls.
/// Outputs quantitative metrics for rule evaluation.
/// </summary>
public sealed class FacadePatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;


    public override string Name => "FacadePatternEvaluator";


    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript"
    ];


    public override string[] SupportedFrameworks =>
    [
        "Application",
        "Service",
        "Domain"
    ];



    private static readonly string[] SupportedExtensions =
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



    private static readonly Regex DirectServiceCallRx =
        new(
            @"\w*(Service|Repository|Client)\.\w+\s*\(",
            RegexOptions.Compiled);



    public FacadePatternEvaluator(
        ILogger<FacadePatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Architecture.DesignPatterns ?? new();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();


        if (!_policy.EnforceFacadePattern)
        {
            _logger.LogInformation(
                "🏛️ Facade pattern enforcement disabled by policy.");

            return results;
        }



        var files =
            EnumerateApplicationFiles(projectPath)
                .Where(IsSupportedFile)
                .ToList();



        if (files.Count == 0)
        {
            _logger.LogInformation(
                "🏛️ No relevant files found for Facade evaluation.");

            return results;
        }



        _logger.LogTrace(
            "🏛️ Scanning {Count} files for Facade pattern metrics.",
            files.Count);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            string content;

            try
            {
                content =
                    await File.ReadAllTextAsync(file, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to read file {File}",
                    file);

                continue;
            }



            int publicMethods =
                PublicMethodRx.Matches(content).Count;


            int dependencies =
                DependencyFieldRx.Matches(content).Count;


            int serviceCalls =
                DirectServiceCallRx.Matches(content).Count;



            double dependencyRatio =
                dependencies /
                (double)Math.Max(
                    1,
                    _policy.MaxDependenciesPerFacade);



            double publicSurfaceRatio =
                publicMethods /
                (double)Math.Max(
                    1,
                    _policy.MaxPublicMethodsPerFacade);



            double orchestrationRatio =
                serviceCalls /
                (double)Math.Max(
                    1,
                    _policy.MaxServiceCallsPerFacade);



            double complianceScore =
                ComputeCompliance(
                    dependencyRatio,
                    publicSurfaceRatio,
                    orchestrationRatio);



            results.Add(
                new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "DesignPattern",

                    Metrics =
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
                            publicSurfaceRatio,

                        ["OrchestrationRatio"] =
                            orchestrationRatio,

                        ["FacadeComplianceScore"] =
                            complianceScore
                    },

                    Metadata =
                    {
                        ["FileName"] =
                            Path.GetFileName(file),

                        ["Language"] =
                            Context?.Language ?? "Unknown",

                        ["Framework"] =
                            Context?.Framework ?? "Unknown",

                        ["Policy_MaxDependenciesPerFacade"] =
                            _policy.MaxDependenciesPerFacade.ToString(),

                        ["Policy_MaxPublicMethodsPerFacade"] =
                            _policy.MaxPublicMethodsPerFacade.ToString(),

                        ["Policy_MaxServiceCallsPerFacade"] =
                            _policy.MaxServiceCallsPerFacade.ToString()
                    }
                });
        }



        if (results.Count > 0)
        {
            double avgCompliance =
                results.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "FacadeComplianceScore",
                            0));


            double avgDependencies =
                results.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "DependencyCount",
                            0));


            double avgMethods =
                results.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "PublicMethodCount",
                            0));



            results.Add(
                new ArchitectureEvaluatorResult(Name, projectPath)
                {
                    Category = "DesignPatternSummary",

                    Metrics =
                    {
                        ["FacadeCount"] =
                            results.Count,

                        ["AverageComplianceScore"] =
                            avgCompliance,

                        ["AverageDependencyCount"] =
                            avgDependencies,

                        ["AveragePublicMethodCount"] =
                            avgMethods,

                        ["OverallFacadeHealth"] =
                            avgCompliance *
                            (1 -
                             avgDependencies /
                             Math.Max(
                                 1,
                                 _policy.MaxDependenciesPerFacade))
                    },

                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["PolicyEnabled"] =
                            _policy.EnforceFacadePattern.ToString()
                    }
                });
        }



        _logger.LogInformation(
            "🏛️ {Evaluator} completed with {Count} metric entries",
            Name,
            results.Count);



        return results;
    }



    private static bool IsSupportedFile(string file)
    {
        return SupportedExtensions.Any(
            ext =>
                file.EndsWith(
                    ext,
                    StringComparison.OrdinalIgnoreCase));
    }



    private static double ComputeCompliance(
        double dependencyRatio,
        double publicRatio,
        double orchestrationRatio)
    {
        double score =
            (1 - Math.Min(dependencyRatio, 1)) * 0.4 +
            (1 - Math.Min(publicRatio, 1)) * 0.3 +
            (1 - Math.Min(orchestrationRatio, 1)) * 0.3;


        return Math.Round(
            score * 100,
            2);
    }
}