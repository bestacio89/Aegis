using Aegis.Core.Architecture.Diagnostics;
using Aegis.Core.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Enums;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Core.Architecture.Evaluators.BackEnd;

/// <summary>
/// Scans code for exception-handling patterns to measure robustness.
/// Collects metrics on empty, generic, or unlogged catch blocks
/// according to <see cref="ErrorHandlingPolicy"/> thresholds.
/// </summary>
public sealed class ErrorHandlingEvaluator : BaseEvaluator, IScopedDependency
{
    private readonly ErrorHandlingPolicy _policy;

    public override string Name => "ErrorHandlingEvaluator";

    public override string[] SupportedLanguages => ["CSharp", "Java", "Python"];
    public override string[] SupportedFrameworks => ["ASP.NET", "Spring Boot", "FastAPI"];

    public ErrorHandlingEvaluator(
        ILogger<ErrorHandlingEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.ErrorHandling ?? new ErrorHandlingPolicy();
    }

    /// <summary>
    /// Core analysis logic — scans each source file and produces EvaluatorResults with exception metrics.
    /// </summary>
    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        // 🎯 Filter files by language
        var extensions = Context?.Language switch
        {
            "CSharp" => new[] { ".cs" },
            "Java" => new[] { ".java" },
            "Python" => new[] { ".py" },
            _ => new[] { ".cs", ".java", ".py" }
        };

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        if (files.Count == 0)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
                $"No {Context?.Language ?? "source"} files found for error-handling evaluation.");
            return results;
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"⚙️ Evaluating exception-handling robustness across {files.Count} {Context?.Language} files.");

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);

            int emptyCatchCount = 0;
            int genericCatchCount = 0;
            int swallowedCount = 0;
            int totalCatchCount = 0;

            // --- Empty catches ---
            var emptyMatches = Regex.Matches(content, @"catch\s*\([^)]+\)\s*\{\s*\}", RegexOptions.Multiline);
            emptyCatchCount = emptyMatches.Count;

            // --- Generic catches ---
            var genericMatches = Regex.Matches(content, @"catch\s*\(\s*Exception\s*\w*\)", RegexOptions.Multiline);
            genericCatchCount = genericMatches.Count;

            // --- Swallowed exceptions (no logging / rethrow) ---
            var swallowMatches = Regex.Matches(content, @"catch\s*\([^)]+\)\s*\{([^}]*)\}", RegexOptions.Singleline);
            foreach (Match m in swallowMatches)
            {
                var body = m.Groups[1].Value;
                bool hasLoggingOrRethrow =
                    body.Contains("throw", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("log", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("Console.Write", StringComparison.OrdinalIgnoreCase) ||
                    body.Contains("print(", StringComparison.OrdinalIgnoreCase);

                if (!hasLoggingOrRethrow)
                    swallowedCount++;
            }

            totalCatchCount = swallowMatches.Count;

            // 🧩 Emit metrics for this file
            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "ErrorHandling",
                Metrics = new Dictionary<string, double>
                {
                    ["EmptyCatchCount"] = emptyCatchCount,
                    ["GenericCatchCount"] = genericCatchCount,
                    ["SwallowedCatchCount"] = swallowedCount,
                    ["TotalCatchCount"] = totalCatchCount
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown",
                    ["AllowEmptyCatch"] = _policy.AllowEmptyCatch.ToString(),
                    ["AllowGenericCatch"] = _policy.AllowGenericCatch.ToString(),
                    ["RequireLoggingOrRethrow"] = _policy.RequireLoggingOrRethrow.ToString()
                }
            });
        }

        // 🧮 Add a global summary for the project
        if (results.Count > 0)
        {
            var totalEmpty = results.Sum(r => r.Metrics.GetValueOrDefault("EmptyCatchCount"));
            var totalGeneric = results.Sum(r => r.Metrics.GetValueOrDefault("GenericCatchCount"));
            var totalSwallowed = results.Sum(r => r.Metrics.GetValueOrDefault("SwallowedCatchCount"));
            var totalFiles = results.Count;

            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "ErrorHandlingSummary",
                Metrics = new Dictionary<string, double>
                {
                    ["TotalFilesScanned"] = totalFiles,
                    ["TotalEmptyCatches"] = totalEmpty,
                    ["TotalGenericCatches"] = totalGeneric,
                    ["TotalSwallowedCatches"] = totalSwallowed
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown"
                }
            });
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
            $"✅ Error-handling analysis complete — {results.Count} metric entries collected.");

        return results;
    }
}
