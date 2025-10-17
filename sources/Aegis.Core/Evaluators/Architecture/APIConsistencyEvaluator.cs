using Aegis.Core.Diagnostics;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Aegis.Shared.Models.Policies;
using Aegis.Shared.Models.Policies.Architecture;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Core.Evaluators.Architecture;

/// <summary>
/// Evaluates REST API route and endpoint consistency across controllers and services.
/// Produces metrics such as lowercase compliance, trailing slashes, and pluralization conflicts.
/// </summary>
public sealed class ApiConsistencyEvaluator : BaseEvaluator, IScopedDependency
{
    private readonly ApiConsistencyPolicy _policy;

    public override string Name => "ApiConsistencyEvaluator";

    public override string[] SupportedLanguages => ["CSharp", "TypeScript", "JavaScript"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Angular", "React", "Vue", "Node"];

    public ApiConsistencyEvaluator(
        ILogger<ApiConsistencyEvaluator> logger,
        IOptions<AegisPolicy> options)
        : base(logger)
    {
        _policy = options.Value.ApiConsistency ?? new ApiConsistencyPolicy();
    }

    /// <summary>
    /// Scans controllers, services, or router files for inconsistent casing, trailing slashes, or pluralization issues.
    /// Returns structured EvaluatorResults for later rule interpretation.
    /// </summary>
    protected override async Task<IEnumerable<EvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<EvaluatorResult>();

        // 🧩 Only scan language-relevant files
        var extensions = Context?.Language switch
        {
            "CSharp" => new[] { ".cs" },
            "TypeScript" => new[] { ".ts" },
            "JavaScript" => new[] { ".js" },
            _ => new[] { ".cs", ".ts", ".js" }
        };

        var files = Directory
            .EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        var routePattern = new Regex(@"Route\s*\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled);
        var apiPattern = new Regex(@"(HttpGet|HttpPost|HttpPut|HttpDelete)\s*\(\s*""?([^"")]+)""?\s*\)", RegexOptions.Compiled);

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace, $"Scanning {files.Count} files for route consistency.");

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token);

            foreach (Match match in routePattern.Matches(content))
            {
                var route = match.Groups[1].Value;
                results.AddRange(EvaluateRoute(file, route));
            }

            foreach (Match match in apiPattern.Matches(content))
            {
                var method = match.Groups[1].Value;
                var path = match.Groups[2].Value;
                results.AddRange(EvaluateEndpoint(file, method, path));
            }
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
            $"API Consistency evaluation produced {results.Count} metric entries.");

        return results;
    }

    private IEnumerable<EvaluatorResult> EvaluateRoute(string file, string route)
    {
        var output = new List<EvaluatorResult>();

        if (_policy.EnforceLowercaseRoutes)
        {
            var hasUppercase = route.Any(char.IsUpper);
            output.Add(new EvaluatorResult(Name, file)
            {
                Category = "Casing",
                Metrics = { ["IsLowercaseCompliant"] = hasUppercase ? 0 : 1 },
                Metadata = new Dictionary<string, string>
                {
                    ["Route"] = route,
                    ["Issue"] = hasUppercase
                        ? "Route contains uppercase characters."
                        : "Route is lowercase compliant."
                }
            });
        }

        if (_policy.EnforceNoTrailingSlash)
        {
            var hasTrailingSlash = route.EndsWith("/", StringComparison.Ordinal);
            output.Add(new EvaluatorResult(Name, file)
            {
                Category = "TrailingSlash",
                Metrics = { ["HasTrailingSlash"] = hasTrailingSlash ? 1 : 0 },
                Metadata = new Dictionary<string, string>
                {
                    ["Route"] = route,
                    ["Issue"] = hasTrailingSlash
                        ? "Route ends with a trailing slash."
                        : "Route does not have trailing slash."
                }
            });
        }

        return output;
    }

    private IEnumerable<EvaluatorResult> EvaluateEndpoint(string file, string method, string path)
    {
        var output = new List<EvaluatorResult>();

        if (_policy.CheckPluralization)
        {
            bool inconsistent = path.Split('/')
                .Any(seg => seg.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                         && seg.Contains("{", StringComparison.Ordinal));

            output.Add(new EvaluatorResult(Name, file)
            {
                Category = "Pluralization",
                Metrics = { ["PluralizationConsistent"] = inconsistent ? 0 : 1 },
                Metadata = new Dictionary<string, string>
                {
                    ["HttpMethod"] = method,
                    ["Path"] = path,
                    ["Issue"] = inconsistent
                        ? "Route mixes plural nouns and parameters."
                        : "Route pluralization consistent."
                }
            });
        }

        return output;
    }
}
