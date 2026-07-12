using System.Text.RegularExpressions;
using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Architecture;

/// <summary>
/// Evaluates API boundary consistency independently of implementation framework.
/// Supports REST style validation across ASP.NET, Spring, Node, Angular services,
/// React API clients, and other HTTP-based ecosystems.
/// Produces ApiConsistencyIndex and supporting governance metrics.
/// </summary>
public sealed class ApiConsistencyEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly ApiConsistencyPolicy _policy;


    public override string Name =>
        "ApiConsistencyEvaluator";


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
        "Spring",
        "Node",
        "Angular",
        "React",
        "Vue"
    ];



    private static readonly Regex RouteRx =
        new(
            @"[""'](\/[a-zA-Z0-9_\-\/\{\}]+)[""']",
            RegexOptions.Compiled);



    private static readonly Regex HttpAttributeRx =
        new(
            @"(HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch|GetMapping|PostMapping|PutMapping|DeleteMapping)",
            RegexOptions.Compiled);



    private static readonly Regex ApiVersionRx =
        new(
            @"\/v\d+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);



    private static readonly Regex UppercaseRouteRx =
        new(
            @"\/[^\s]*[A-Z]",
            RegexOptions.Compiled);



    private static readonly Regex TrailingSlashRx =
        new(
            @"\/+$",
            RegexOptions.Compiled);



    private static readonly Regex DuplicateSegmentRx =
        new(
            @"\/([^\/]+)\/\1",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);



    public ApiConsistencyEvaluator(
        ILogger<ApiConsistencyEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.ApiConsistency
            ?? new ApiConsistencyPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        if (Context is null)
        {
            _logger.LogWarning(
                "{Evaluator} skipped. Missing architecture context.",
                Name);

            return results;
        }



        var files =
            ResolveApiFiles(
                projectPath,
                Context);



        if (!files.Any())
        {
            _logger.LogDebug(
                "{Evaluator}: no API files detected.",
                Name);

            return results;
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Analyzing {files.Count()} API boundary files.");



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            string content;

            try
            {
                content =
                    await File.ReadAllTextAsync(
                        file,
                        token);
            }
            catch
            {
                continue;
            }


            results.Add(
                EvaluateFile(
                    file,
                    content));
        }



        if (results.Count > 0)
            AddSummary(
                results,
                projectPath);



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} results.",
            Name,
            results.Count);


        return results;
    }




    private ArchitectureEvaluatorResult EvaluateFile(
        string file,
        string content)
    {
        var routes =
            RouteRx.Matches(content)
                .Select(x => x.Groups[1].Value)
                .Distinct()
                .ToList();



        int uppercaseViolations = 0;
        int trailingSlashViolations = 0;
        int duplicateSegments = 0;
        int missingVersioning = 0;



        foreach (var route in routes)
        {
            if (_policy.EnforceLowercaseRoutes &&
                UppercaseRouteRx.IsMatch(route))
            {
                uppercaseViolations++;
            }


            if (_policy.EnforceNoTrailingSlash &&
                TrailingSlashRx.IsMatch(route))
            {
                trailingSlashViolations++;
            }


            if (DuplicateSegmentRx.IsMatch(route))
            {
                duplicateSegments++;
            }


        
        }



        int endpointCount =
            HttpAttributeRx.Matches(content).Count;



        double routeScore =
            ComputeScore(
                routes.Count,
                uppercaseViolations);



        double slashScore =
            ComputeScore(
                routes.Count,
                trailingSlashViolations);



        double duplicationScore =
            ComputeScore(
                routes.Count,
                duplicateSegments);



        double versionScore =
            ComputeScore(
                routes.Count,
                missingVersioning);



        double maturityScore =
            endpointCount > 0
                ? 100
                : 0;



        double apiConsistency =
            routeScore * 0.35 +
            slashScore * 0.15 +
            duplicationScore * 0.15 +
            versionScore * 0.20 +
            maturityScore * 0.15;



        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            Category = "Architecture",

            Metrics =
            {
                ["RouteConsistencyScore"] = routeScore,
                ["TrailingSlashScore"] = slashScore,
                ["RouteDuplicationScore"] = duplicationScore,
                ["ApiVersioningScore"] = versionScore,
                ["EndpointDiscoveryScore"] = maturityScore,

                ["UppercaseRouteViolations"] =
                    uppercaseViolations,

                ["TrailingSlashViolations"] =
                    trailingSlashViolations,

                ["DuplicateRouteViolations"] =
                    duplicateSegments,

                ["MissingVersionViolations"] =
                    missingVersioning,


                ["ApiConsistencyIndex"] =
                    Math.Round(
                        apiConsistency,
                        2)
            },

            Metadata =
            {
                ["FileName"] =
                    Path.GetFileName(file),

                ["Language"] =
                    Context?.Language
                    ?? "Unknown",

                ["Framework"] =
                    Context?.Framework
                    ?? "Unknown",

                ["RouteCount"] =
                    routes.Count.ToString()
            }
        };
    }





    private IEnumerable<string> ResolveApiFiles(
        string projectPath,
        ProjectArchitectureContext context)
    {
        var extensions =
            context.Language switch
            {
                "C#" =>
                    new[] { ".cs" },

                "Java" =>
                    new[] { ".java" },

                "TypeScript" =>
                    new[] { ".ts" },

                "JavaScript" =>
                    new[] { ".js" },

                _ =>
                    Array.Empty<string>()
            };


        return Directory
            .EnumerateFiles(
                projectPath,
                "*.*",
                SearchOption.AllDirectories)
            .Where(f =>
                extensions.Any(x =>
                    f.EndsWith(
                        x,
                        StringComparison.OrdinalIgnoreCase)))
            .Where(f =>
                !IsExcludedDir(f))
            .Where(f =>
                f.Contains(
                    "controller",
                    StringComparison.OrdinalIgnoreCase)
                ||
                f.Contains(
                    "api",
                    StringComparison.OrdinalIgnoreCase)
                ||
                f.Contains(
                    "route",
                    StringComparison.OrdinalIgnoreCase));
    }





    private static double ComputeScore(
        int total,
        int violations)
    {
        if (total == 0)
            return 100;


        return Math.Round(
            Math.Max(
                0,
                100 -
                violations * 100.0 / total),
            2);
    }





    private static void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var average =
            results.Average(
                r =>
                r.Metrics.GetValueOrDefault(
                    "ApiConsistencyIndex",
                    0));



        results.Add(
            new ArchitectureEvaluatorResult(
                "ApiConsistencyEvaluator",
                projectPath)
            {
                Category =
                    "ArchitectureSummary",

                Metrics =
                {
                    ["AnalyzedFiles"] =
                        results.Count,

                    ["AverageApiConsistencyIndex"] =
                        average,

                    ["ApiMaturityIndex"] =
                        average
                },

                Metadata =
                {
                    ["Evaluator"] =
                        "ApiConsistencyEvaluator"
                }
            });
    }
}