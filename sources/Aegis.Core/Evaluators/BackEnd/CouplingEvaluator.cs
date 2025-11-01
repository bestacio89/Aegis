using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Enums;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.BackEnd;

/// <summary>
/// Evaluates inter-module and external coupling across multi-language projects.
/// Emits structured EvaluatorResults with import and dependency metrics.
/// </summary>
public sealed class CouplingEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly CouplingPolicy _policy;

    public override string Name => "CouplingEvaluator";

    public override string[] SupportedLanguages => ["CSharp", "Java", "Python", "TypeScript", "JavaScript"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring Boot", "FastAPI", "Angular", "React", "Node"];

    public CouplingEvaluator(
        ILogger<CouplingEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Coupling ?? new CouplingPolicy();
    }

    /// <summary>
    /// Scans for coupling and dependency metrics, emitting EvaluatorResults for the RuleEngine.
    /// </summary>
    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        var langRoots = new[] { "DotNet", "Java", "Python", "Angular", "React", "JsTs" }
            .Select(p => Path.Combine(projectPath, p))
            .Where(Directory.Exists)
            .ToList();

        if (langRoots.Count == 0)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                "No language-specific submodules detected for coupling analysis.");
            return results;
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🔍 Beginning coupling analysis across {langRoots.Count} language module(s) ({Context?.Language}/{Context?.Framework}).");

        foreach (var langDir in langRoots)
        {
            token.ThrowIfCancellationRequested();

            var language = Path.GetFileName(langDir);
            AegisDiagnostics.Report(Name, DiagnosticLevel.Trace, $"Evaluating coupling in {language} module...");

            var files = Directory.EnumerateFiles(langDir, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                    f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".java", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                .Where(f => !IsExcludedDir(f))
                .ToList();

            foreach (var file in files)
            {
                var content = await File.ReadAllTextAsync(file, token);
                int importCount = CountImports(language, content);

                results.Add(new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "Coupling",
                    Metrics = new Dictionary<string, double>
                    {
                        ["ImportCount"] = importCount,
                        ["MaxImportsThreshold"] = _policy.MaxImportsPerFile
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["Language"] = language,
                        ["Framework"] = Context?.Framework ?? "Unknown",
                        ["Policy_MaxExternalDependencies"] = _policy.MaxExternalDependencies.ToString(),
                        ["DeepDependencyScan"] = _policy.DeepDependencyScan.ToString()
                    }
                });
            }

            if (_policy.DeepDependencyScan)
            {
                var dependencyResults = await ScanDependencyManifestsAsync(language, langDir, token);
                results.AddRange(dependencyResults);
            }
        }

        AegisDiagnostics.Report(Name,
            results.Count > 0 ? DiagnosticLevel.Trace : DiagnosticLevel.Info,
            $"🧩 Coupling analysis complete with {results.Count} metric entries.");

        return results;
    }

    // ------------------------------------------------------
    // Helpers
    // ------------------------------------------------------

    private static int CountImports(string language, string content)
    {
        return language switch
        {
            "DotNet" => Regex.Matches(content, @"\busing\s+[A-Za-z0-9_.]+;").Count,
            "Java" => Regex.Matches(content, @"\bimport\s+[A-Za-z0-9_.]+;").Count,
            "Python" => Regex.Matches(content, @"\bimport\s+[A-Za-z0-9_\.]+").Count,
            "Angular" or "React" or "JsTs" =>
                Regex.Matches(content, @"\bimport\s+.*?from\s+['""][^'""]+['""];").Count,
            _ => 0
        };
    }

    private async Task<IEnumerable<ArchitectureEvaluatorResult>> ScanDependencyManifestsAsync(
        string language, string langDir, CancellationToken token)
    {
        var manifestResults = new List<ArchitectureEvaluatorResult>();

         Task AddResultAsync(string file, int dependencyCount)
        {
            manifestResults.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Coupling",
                Metrics = new Dictionary<string, double>
                {
                    ["DependencyCount"] = dependencyCount,
                    ["MaxDependencyThreshold"] = _policy.MaxExternalDependencies
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Language"] = language,
                    ["ManifestFile"] = Path.GetFileName(file)
                }
            });
            return Task.CompletedTask;
        }

        if (language is "DotNet")
        {
            foreach (var csproj in Directory.EnumerateFiles(langDir, "*.csproj", SearchOption.AllDirectories))
            {
                var xml = await File.ReadAllTextAsync(csproj, token);
                int total = Regex.Matches(xml, @"<ProjectReference").Count +
                            Regex.Matches(xml, @"<PackageReference").Count;
                await AddResultAsync(csproj, total);
            }
        }
        else if (language is "Java")
        {
            foreach (var pom in Directory.EnumerateFiles(langDir, "pom.xml", SearchOption.AllDirectories))
            {
                var xml = await File.ReadAllTextAsync(pom, token);
                int depCount = Regex.Matches(xml, @"<dependency>").Count;
                await AddResultAsync(pom, depCount);
            }
        }
        else if (language is "Python")
        {
            foreach (var req in Directory.EnumerateFiles(langDir, "requirements.txt", SearchOption.AllDirectories))
            {
                int lines = (await File.ReadAllLinesAsync(req, token))
                    .Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"));
                await AddResultAsync(req, lines);
            }
        }
        else if (language is "Angular" or "React" or "JsTs")
        {
            foreach (var pkg in Directory.EnumerateFiles(langDir, "package.json", SearchOption.AllDirectories))
            {
                var json = await File.ReadAllTextAsync(pkg, token);
                int deps = Regex.Matches(json, "\"[A-Za-z0-9_-]+\":\\s*\"").Count;
                await AddResultAsync(pkg, deps);
            }
        }

        return manifestResults;
    }
}
