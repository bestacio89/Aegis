
using System.Text.RegularExpressions;
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
/// Evaluates Node.js and TypeScript backend source for configured runtime,
/// module-system, and asynchronous-code patterns.
///
/// This evaluator consumes the project context established by detectors.
/// It emits file-level evidence; RuleEngineCore owns compliance decisions.
///
/// Regex-based detections are heuristic and must not be interpreted as
/// compiler-grade proof that a defect exists.
/// </summary>
public sealed class NodeEvaluator :
    BaseArchitectureEvaluator,
    IScopedDependency
{
    private readonly NodePolicy _policy;

    public override string Name => "NodeEvaluator";

    public override string[] SupportedLanguages =>
    [
        "JavaScript",
        "TypeScript"
    ];

    public override string[] SupportedFrameworks =>
    [
        "Node",
        "NestJS",
        "Next.js"
    ];

    private static readonly Regex EvalRegex = new(
        @"\beval\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ChildProcessRegex = new(
        @"(?:\brequire\s*\(\s*['""]child_process['""]\s*\)|\bfrom\s+['""]child_process['""]|\bimport\s*\(\s*['""]child_process['""]\s*\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CallbackNestingRegex = new(
        @"(?:function\s*\([^)]*\)|\([^)]*\)\s*=>|[A-Za-z_$][\w$]*\s*=>)\s*\{[^{}]{0,500}(?:function\s*\([^)]*\)|\([^)]*\)\s*=>|[A-Za-z_$][\w$]*\s*=>)\s*\{",
        RegexOptions.Compiled
        | RegexOptions.CultureInvariant
        | RegexOptions.Singleline);

    private static readonly Regex AsyncFunctionRegex = new(
        @"\basync\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RequireRegex = new(
        @"\brequire\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TryCatchRegex = new(
        @"\btry\s*\{[\s\S]{0,3000}?\}\s*catch\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CatchCallRegex = new(
        @"\.catch\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules",
            "generated",
            "dist",
            "build",
            "coverage",
            "test",
            "tests",
            "obj",
            "bin",
            ".git"
        };

    public NodeEvaluator(
        ILogger<NodeEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Node ?? new NodePolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (Context is null)
        {
            return [];
        }

        if (!IsSupportedNodeProject(Context))
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Node evaluation skipped because the project context is unsupported.");

            return [];
        }

        var extensions = Context.Language.Equals(
            "TypeScript",
            StringComparison.OrdinalIgnoreCase)
            ? new[] { ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs" }
            : new[] { ".js", ".jsx", ".mjs", ".cjs" };

        var files = ResolveSourceFiles(projectPath, extensions)
            .Where(IsEligibleSourceFile)
            .ToList();

        var results = new List<ArchitectureEvaluatorResult>();

        var filesRead = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not read Node source file {SourceFile}.",
                    file);

                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Access denied reading Node source file {SourceFile}.",
                    file);

                continue;
            }

            token.ThrowIfCancellationRequested();

            filesRead++;

            EvaluateSecurityPatterns(file, content, results);
            EvaluateAsyncPatterns(file, content, results);
            EvaluateModuleSystem(file, content, results);
        }

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Node evaluation completed. Files discovered={files.Count}; " +
            $"files read={filesRead}; evidence facts={results.Count}.");

        return results;
    }

    private void EvaluateSecurityPatterns(
        string file,
        string content,
        List<ArchitectureEvaluatorResult> results)
    {
        if (_policy.DisallowEval)
        {
            AddPatternFinding(
                file,
                content,
                EvalRegex,
                "NODE001",
                "NodeDynamicEvalCount",
                "Security",
                "Dynamic eval invocation detected. Review whether input can influence the evaluated code.",
                "Medium",
                results);
        }

        if (_policy.DisallowChildProcess)
        {
            AddPatternFinding(
                file,
                content,
                ChildProcessRegex,
                "NODE002",
                "NodeChildProcessUsageCount",
                "Security",
                "child_process usage detected. Review command construction, argument handling, and execution permissions.",
                "Medium",
                results);
        }

        AddPatternFinding(
            file,
            content,
            CallbackNestingRegex,
            "NODE003",
            "NodeNestedCallbackPatternCount",
            "Maintainability",
            "A nested callback pattern was detected. Review control-flow complexity; this heuristic does not prove excessive nesting.",
            "Low",
            results);
    }

    private void EvaluateAsyncPatterns(
        string file,
        string content,
        List<ArchitectureEvaluatorResult> results)
    {
        if (!_policy.CheckUnhandledPromises)
        {
            return;
        }

        // A file-level absence of .catch() is not proof of an unhandled
        // rejection. Only emit a review signal when async code is present
        // and no obvious try/catch or .catch() pattern exists in the file.
        if (!AsyncFunctionRegex.IsMatch(content))
        {
            return;
        }

        if (TryCatchRegex.IsMatch(content) || CatchCallRegex.IsMatch(content))
        {
            return;
        }

        AddFinding(
            file,
            "NODE004",
            "NodeAsyncErrorHandlingReviewCount",
            1,
            "Reliability",
            "Async code was detected, but no obvious try/catch or .catch() pattern was found in this file. Review promise rejection handling at the relevant call sites.",
            "Low",
            "Heuristic: file-level pattern absence; not proof of an unhandled promise.");
    }

    private void EvaluateModuleSystem(
        string file,
        string content,
        List<ArchitectureEvaluatorResult> results)
    {
        if (!_policy.EnforceESModules || !RequireRegex.IsMatch(content))
        {
            return;
        }

        var contextIndicatesEsm = Context?.PatternsDetected.Any(
            pattern => pattern.Equals(
                "ESModule",
                StringComparison.OrdinalIgnoreCase)) == true;

        if (!contextIndicatesEsm)
        {
            return;
        }

        AddPatternFinding(
            file,
            content,
            RequireRegex,
            "NODE005",
            "NodeCommonJsRequireInEsmCount",
            "Architecture",
            "CommonJS require() usage was detected in a project identified as using ES modules. Review whether this usage is intentional and supported by the runtime configuration.",
            "Medium",
            results);
    }

    private void AddPatternFinding(
        string file,
        string content,
        Regex regex,
        string rule,
        string metricKey,
        string category,
        string message,
        string confidence,
        List<ArchitectureEvaluatorResult> results)
    {
        var matches = regex.Matches(content);

        if (matches.Count == 0)
        {
            return;
        }

        var lineNumbers = matches
            .Cast<Match>()
            .Select(match =>
                1 + content.AsSpan(0, match.Index)
                    .Count('\n'))
            .Distinct()
            .OrderBy(line => line)
            .ToArray();

        AddFinding(
            file,
            rule,
            metricKey,
            matches.Count,
            category,
            message,
            confidence,
            $"Detected at line(s): {string.Join(", ", lineNumbers)}.",
            results);
    }

    private void AddFinding(
        string file,
        string rule,
        string metricKey,
        double occurrenceCount,
        string category,
        string message,
        string confidence,
        string evidence,
        List<ArchitectureEvaluatorResult>? results = null)
    {
        var result = CreateResult(
            file,
            nameof(ArchitectureRuleCategory.Architecture));

        result.Metrics[metricKey] = occurrenceCount;

        result.Metadata["Rule"] = rule;
        result.Metadata["Message"] = message;
        result.Metadata["FindingCategory"] = category;
        result.Metadata["DetectionConfidence"] = confidence;
        result.Metadata["Evidence"] = evidence;
        result.Metadata["Language"] = Context?.Language ?? "Unknown";
        result.Metadata["Framework"] = Context?.Framework ?? "Unknown";
        result.Metadata["Layer"] = ResolveLayer(file) ?? "Unknown";
        result.Metadata["MetricScope"] = FileScope;

        if (results is not null)
        {
            results.Add(result);
        }
    }

    private static bool IsSupportedNodeProject(
        ProjectArchitectureContext context)
    {
        if (context.Language is not ("JavaScript" or "TypeScript"))
        {
            return false;
        }

        var framework = context.Framework ?? string.Empty;

        return framework.Contains("Node", StringComparison.OrdinalIgnoreCase)
            || framework.Contains("Nest", StringComparison.OrdinalIgnoreCase)
            || framework.Contains("Next", StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveLayer(string file)
    {
        var fullPath = Path.GetFullPath(file);

        return Context?.Layers.FirstOrDefault(layer =>
            layer.Files.Any(layerFile =>
                string.Equals(
                    Path.GetFullPath(layerFile),
                    fullPath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal)))?.Name;
    }

    private static bool IsEligibleSourceFile(string file)
    {
        var normalized = file.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);

        var segments = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(ExcludedDirectoryNames.Contains))
        {
            return false;
        }

        var name = Path.GetFileName(file);

        return !name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".generated.js", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".generated.ts", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);
    }
}
