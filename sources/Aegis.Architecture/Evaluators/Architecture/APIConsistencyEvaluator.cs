
using System.Text.Json;
using System.Text.RegularExpressions;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Architecture;

/// <summary>
/// Evaluates API boundary consistency independently of implementation framework.
/// Produces file-level metrics and a separate project-level summary.
/// </summary>
public sealed class ApiConsistencyEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly ApiConsistencyPolicy _policy;

    public override string Name => "ApiConsistencyEvaluator";

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

    private static readonly Regex RouteRx = new(
        @"[""'](\/?[a-zA-Z0-9_\-\/\{\}]+)[""']",
        RegexOptions.Compiled);

    private static readonly Regex HttpAttributeRx = new(
        @"(HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch|GetMapping|PostMapping|PutMapping|DeleteMapping)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ApiVersionRx = new(
        @"/v\d+(?=/|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UppercaseRouteRx = new(
        @"/[^/\s]*[A-Z]",
        RegexOptions.Compiled);

    private static readonly Regex TrailingSlashRx = new(
        @"/+$",
        RegexOptions.Compiled);

    private static readonly Regex DuplicateSegmentRx = new(
        @"/([^/]+)/\1(?:/|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ApiConsistencyEvaluator(
        ILogger<ApiConsistencyEvaluator> logger,
        IOptions<ApiConsistencyPolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value
            ?? new ApiConsistencyPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        if (Context is null)
        {
            _logger.LogWarning(
                "{Evaluator} skipped. Missing architecture context.",
                Name);

            return [];
        }

        var files = ResolveSourceFiles(
                projectPath,
                GetExtensions(Context.Language))
            .Where(IsApiBoundaryFile)
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogDebug(
                "{Evaluator}: no API files detected.",
                Name);

            return [];
        }

        _logger.LogDebug(
            "API policy: Lowercase={Lowercase}, NoTrailingSlash={NoTrailingSlash}, Pluralization={Pluralization}",
            _policy.EnforceLowercaseRoutes,
            _policy.EnforceNoTrailingSlash,
            _policy.CheckPluralization);

        var results = new List<ArchitectureEvaluatorResult>();

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Evaluator} could not read API file {File}.",
                    Name,
                    file);

                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Evaluator} could not access API file {File}.",
                    Name,
                    file);

                continue;
            }

            results.Add(EvaluateFile(file, content));
        }

        if (results.Count > 0)
        {
            AddSummary(results, projectPath);
        }

        _logger.LogInformation(
            "{Evaluator} completed with {Count} file and summary results.",
            Name,
            results.Count);

        return results;
    }

    private ArchitectureEvaluatorResult EvaluateFile(
        string file,
        string content)
    {
        var routes = RouteRx
            .Matches(content)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var uppercaseViolations = 0;
        var trailingSlashViolations = 0;
        var duplicateSegments = 0;
        var missingVersioning = 0;

        foreach (var route in routes)
        {
            if (_policy.EnforceLowercaseRoutes
                && UppercaseRouteRx.IsMatch(route))
            {
                uppercaseViolations++;
            }

            if (_policy.EnforceNoTrailingSlash
                && TrailingSlashRx.IsMatch(route))
            {
                trailingSlashViolations++;
            }

            if (_policy.CheckPluralization
                && DuplicateSegmentRx.IsMatch(route))
            {
                duplicateSegments++;
            }

            if (!ApiVersionRx.IsMatch(route))
            {
                missingVersioning++;
            }
        }

        var endpointCount = HttpAttributeRx.Matches(content).Count;

        var routeScore = ComputeScore(
            routes.Count,
            uppercaseViolations);

        var slashScore = ComputeScore(
            routes.Count,
            trailingSlashViolations);

        var duplicationScore = ComputeScore(
            routes.Count,
            duplicateSegments);

        var versionScore = ComputeScore(
            routes.Count,
            missingVersioning);

        var maturityScore = endpointCount > 0 ? 100d : 0d;

        var apiConsistency =
            routeScore * 0.35
            + slashScore * 0.15
            + duplicationScore * 0.15
            + versionScore * 0.20
            + maturityScore * 0.15;

        var result = CreateResult(
            file,
            nameof(ArchitectureRuleCategory.Architecture));

        result.Metrics["RouteConsistencyScore"] = routeScore;
        result.Metrics["TrailingSlashScore"] = slashScore;
        result.Metrics["RouteDuplicationScore"] = duplicationScore;
        result.Metrics["ApiVersioningScore"] = versionScore;
        result.Metrics["EndpointDiscoveryScore"] = maturityScore;

        result.Metrics["UppercaseRouteViolations"] =
            uppercaseViolations;

        result.Metrics["TrailingSlashViolations"] =
            trailingSlashViolations;

        result.Metrics["DuplicateRouteViolations"] =
            duplicateSegments;

        result.Metrics["MissingVersionViolations"] =
            missingVersioning;

        result.Metrics["ApiConsistencyIndex"] =
            Math.Round(apiConsistency, 2);

        result.Metadata["FileName"] = Path.GetFileName(file);
        result.Metadata["Language"] = Context?.Language ?? "Unknown";
        result.Metadata["Framework"] = Context?.Framework ?? "Unknown";
        result.Metadata["RouteCount"] = routes.Count.ToString();
        result.Metadata["Routes"] = JsonSerializer.Serialize(routes);

        return result;
    }

    private static string[] GetExtensions(string language) =>
        language switch
        {
            "C#" => [".cs"],
            "Java" => [".java"],
            "TypeScript" => [".ts"],
            "JavaScript" => [".js"],
            _ => []
        };

    private static bool IsApiBoundaryFile(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);

        return name.Contains(
                   "controller",
                   StringComparison.OrdinalIgnoreCase)
            || name.Contains(
                   "endpoint",
                   StringComparison.OrdinalIgnoreCase)
            || name.Contains(
                   "route",
                   StringComparison.OrdinalIgnoreCase)
            || name.Contains(
                   "resource",
                   StringComparison.OrdinalIgnoreCase)
            || name.Contains(
                   "api",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static double ComputeScore(int total, int violations)
    {
        if (total == 0)
        {
            return 100;
        }

        return Math.Round(
            Math.Max(0, 100 - violations * 100.0 / total),
            2);
    }

    private static void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var analyzedFiles = results.Count;

        var average = results.Average(
            result => result.Metrics.GetValueOrDefault(
                "ApiConsistencyIndex",
                0));

        results.Add(
            new ArchitectureEvaluatorResult(
                "ApiConsistencyEvaluator",
                projectPath)
            {
                Category = "ArchitectureSummary",
                Metrics =
                {
                    ["AnalyzedFiles"] = analyzedFiles,
                    ["AverageApiConsistencyIndex"] = average,
                    ["ApiMaturityIndex"] = average
                },
                Metadata =
                {
                    [EvaluationScopeMetadataKey] = ProjectScope,
                    ["Evaluator"] = "ApiConsistencyEvaluator"
                }
            });
    }
}
