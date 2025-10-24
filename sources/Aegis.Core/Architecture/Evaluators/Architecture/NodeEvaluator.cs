using Aegis.Core.Architecture.Diagnostics;
using Aegis.Core.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Enums;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Core.Architecture.Evaluators.Architecture;

/// <summary>
/// Scans Node.js and TypeScript backends for unsafe or non-recommended practices.
/// Produces structured EvaluatorResults describing risky constructs and metrics
/// for policy enforcement by the RuleEngine.
/// </summary>
public sealed class NodeEvaluator : BaseEvaluator, IScopedDependency
{
    public override string Name => "NodeEvaluator";

    public override string[] SupportedLanguages => ["JavaScript", "TypeScript"];
    public override string[] SupportedFrameworks => ["Node", "NestJS", "Next.js"];

    private readonly NodePolicy _policy;

    // Regex detectors
    private static readonly Regex EvalRx = new(@"\beval\s*\(", RegexOptions.Compiled);
    private static readonly Regex ChildProcRx = new(@"\brequire\s*\(['""]child_process['""]\)", RegexOptions.Compiled);
    private static readonly Regex NestedCallbackRx = new(@"function\s*\([^)]*\)\s*\{[^{}]*(function\s*\([^)]*\)\s*\{){3,}", RegexOptions.Compiled);
    private static readonly Regex AsyncRx = new(@"\basync\s+function\s+\w+", RegexOptions.Compiled);

    public NodeEvaluator(ILogger<NodeEvaluator> logger, IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Node ?? new NodePolicy();
    }

    /// <summary>
    /// Executes analysis for Node.js projects and emits EvaluatorResults.
    /// </summary>
    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f =>
                f.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsExcludedDir(f))
            .ToList();

        if (files.Count == 0)
        {
            AegisDiagnostics.Report(Name, DiagnosticLevel.Info, "No Node.js/TypeScript files detected for evaluation.");
            return results;
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🟢 Scanning {files.Count} file(s) for Node.js pattern compliance ({Context?.Framework ?? "Generic Node"}).");

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);

            // Initialize per-file result record
            var evalResult = new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "NodeSecurity",
                Metrics = new Dictionary<string, double>
                {
                    ["UsesEval"] = 0,
                    ["UsesChildProcess"] = 0,
                    ["HasCallbackHell"] = 0,
                    ["UnhandledPromise"] = 0,
                    ["RiskyPackageScript"] = 0,
                    ["CommonJsRequire"] = 0
                },
                Metadata = new Dictionary<string, string>
                {
                    ["Framework"] = Context?.Framework ?? "Unknown",
                    ["Language"] = Context?.Language ?? "JavaScript"
                }
            };

            // 1️⃣ eval()
            if (_policy.DisallowEval && EvalRx.IsMatch(content))
            {
                evalResult.Metrics["UsesEval"] = 1;
                evalResult.Metadata["EvalFound"] = "true";
            }

            // 2️⃣ child_process
            if (_policy.DisallowChildProcess && ChildProcRx.IsMatch(content))
            {
                evalResult.Metrics["UsesChildProcess"] = 1;
                evalResult.Metadata["ChildProcessFound"] = "true";
            }

            // 3️⃣ Nested callbacks (callback hell)
            if (NestedCallbackRx.IsMatch(content))
            {
                evalResult.Metrics["HasCallbackHell"] = 1;
                evalResult.Metadata["CallbackDepth"] = "3+ nested";
            }

            // 4️⃣ Async functions without catch()
            if (_policy.CheckUnhandledPromises && AsyncRx.IsMatch(content) && !content.Contains(".catch"))
            {
                evalResult.Metrics["UnhandledPromise"] = 1;
                evalResult.Metadata["UnhandledAsync"] = "true";
            }

            // 5️⃣ Risky npm scripts in package.json
            if (_policy.ValidatePackageJsonScripts &&
                Path.GetFileName(file).Equals("package.json", StringComparison.OrdinalIgnoreCase))
            {
                if (content.Contains("rm -rf", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("curl", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("wget", StringComparison.OrdinalIgnoreCase))
                {
                    evalResult.Metrics["RiskyPackageScript"] = 1;
                    evalResult.Metadata["RiskyScript"] = "true";
                }
            }

            // 6️⃣ Deprecated require() in ESM context
            if (_policy.EnforceESModules && content.Contains("require(", StringComparison.OrdinalIgnoreCase))
            {
                evalResult.Metrics["CommonJsRequire"] = 1;
                evalResult.Metadata["CommonJS"] = "true";
            }

            // Only add result if any nonzero metric was found
            if (evalResult.Metrics.Values.Any(v => v > 0))
                results.Add(evalResult);
        }

        AegisDiagnostics.Report(Name,
            results.Count > 0 ? DiagnosticLevel.Warning : DiagnosticLevel.Info,
            $"🟢 Node.js evaluation complete with {results.Count} issue(s) flagged.");

        return results;
    }
}
