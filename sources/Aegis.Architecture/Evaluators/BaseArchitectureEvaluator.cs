
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Contracts;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Utilities;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Evaluators;

/// <summary>
/// Base abstraction for Aegis evaluators.
///
/// Evaluators analyze application code and produce structured facts.
/// Rule interpretation and compliance decisions belong to RuleEngineCore.
/// </summary>
public abstract class BaseArchitectureEvaluator : IEvaluator, IScopedDependency
{
    /// <summary>
    /// Metadata key used to distinguish file-level facts from aggregate facts.
    /// </summary>
    public const string EvaluationScopeMetadataKey = "EvaluationScope";

    /// <summary>
    /// Scope identifier for facts describing an individual source file.
    /// </summary>
    public const string FileScope = "File";

    /// <summary>
    /// Scope identifier for project-wide aggregate facts.
    /// </summary>
    public const string ProjectScope = "Project";

    /// <summary>
    /// Metadata key containing the actual source file path.
    /// </summary>
    public const string FilePathMetadataKey = "FilePath";

    protected readonly ILogger _logger;

    protected ProjectArchitectureContext? Context { get; private set; }

    public abstract string Name { get; }

    public virtual string[] SupportedLanguages { get; } = ["*"];

    public virtual string[] SupportedFrameworks { get; } = ["*"];

    public bool IsEnabled { get; set; } = true;

    public double WeightFactor { get; set; } = 1.0;

    protected BaseArchitectureEvaluator(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Applies evaluator-specific policy settings.
    /// Evaluators not explicitly handled here remain enabled by default.
    /// </summary>
    public virtual void ApplyPolicy(AegisArchitecturePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        switch (Name)
        {
            case "PerformanceEvaluator":
                IsEnabled = policy.Performance.Enabled;
                WeightFactor = 1.2;
                break;

            case "MaintainabilityEvaluator":
                IsEnabled =
                    policy.Maintainability.MinMaintainabilityIndex > 0;
                WeightFactor = 1.0;
                break;

            case "SecurityEvaluator":
                IsEnabled = policy.Security.Enabled;
                WeightFactor = 1.5;
                break;

            default:
                IsEnabled = true;
                WeightFactor = 1.0;
                break;
        }

        _logger.LogDebug(
            "Evaluator {Name}: Enabled={Enabled}, Weight={Weight}",
            Name,
            IsEnabled,
            WeightFactor);
    }

    /// <summary>
    /// Executes this evaluator and normalizes the metadata of its results.
    /// Results are materialized once so deferred enumerables are not enumerated
    /// repeatedly and returned results are stable.
    /// </summary>
    public async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateAsync(
        string projectPath,
        ProjectArchitectureContext context,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(context);

        token.ThrowIfCancellationRequested();

        Context = context;

        if (!IsEnabled)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Info,
                $"Evaluator disabled by policy: {Name}");

            return [];
        }

        try
        {
            var evaluated = await EvaluateCoreAsync(projectPath, token);

            if (evaluated is null)
            {
                throw new InvalidOperationException(
                    $"Evaluator '{Name}' returned a null result sequence.");
            }

            var results = evaluated.ToList();

            foreach (var result in results)
            {
                token.ThrowIfCancellationRequested();

                if (result is null)
                {
                    throw new InvalidOperationException(
                        $"Evaluator '{Name}' returned a null result.");
                }

                result.WeightFactor = WeightFactor;
                result.WasEnabled = true;

                // Infer file scope only when a real file-path metadata entry
                // exists. Do not infer scope from Target: it may identify a
                // project, module, component, or other aggregate.
                if (result.Metadata is not null
                    && result.Metadata.ContainsKey(FilePathMetadataKey)
                    && !result.Metadata.ContainsKey(EvaluationScopeMetadataKey))
                {
                    result.Metadata[EvaluationScopeMetadataKey] = FileScope;
                }
            }

            token.ThrowIfCancellationRequested();

            return results;
        }
        catch (OperationCanceledException)
        {
            // Cancellation must propagate in both Debug and Release builds.
            throw;
        }
        catch (Exception ex)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Error,
                $"Evaluator failed: {Name}",
                ex);

            _logger.LogError(
                ex,
                "Evaluator {EvaluatorName} failed for project {ProjectPath}.",
                Name,
                projectPath);

#if DEBUG
            throw;
#else
            return [];
#endif
        }
    }

    /// <summary>
    /// Performs the evaluator-specific analysis.
    /// Implementations should observe the cancellation token during long scans.
    /// </summary>
    protected abstract Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token);

    /// <summary>
    /// Enumerates source files matching the requested extensions while
    /// restricting analysis to the application's backend/frontend boundaries.
    /// </summary>
    protected IReadOnlyList<string> ResolveSourceFiles(
        string projectPath,
        params string[] extensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(extensions);

        if (extensions.Length == 0)
        {
            return [];
        }

        return Directory
            .EnumerateFiles(
                projectPath,
                "*.*",
                SearchOption.AllDirectories)
            .Where(file => extensions.Any(extension =>
                !string.IsNullOrWhiteSpace(extension)
                && file.EndsWith(
                    extension,
                    StringComparison.OrdinalIgnoreCase)))
            .Where(file => !IsExcludedDir(file))
            .Where(IsApplicationCode)
            .ToList();
    }

    /// <summary>
    /// Returns true when a file is within the supported backend/frontend
    /// application boundaries and outside known generated or external folders.
    /// </summary>
    private static bool IsApplicationCode(string file)
    {
        var normalized = file.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);

        var ignoredSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}client{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Temp{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Logs{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"
        };

        if (ignoredSegments.Any(segment =>
                normalized.Contains(
                    segment,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var pathParts = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        return pathParts.Any(part =>
            part.Equals("back", StringComparison.OrdinalIgnoreCase)
            || part.Equals("front", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Delegates generated, temporary, and other excluded-path checks to the
    /// shared Aegis path-filtering utility.
    /// </summary>
    protected static bool IsExcludedDir(string path) =>
        PathUtils.IsExcludedDir(path);

    /// <summary>
    /// Creates a file-level evaluator fact using the filename as Target and
    /// the original path in metadata for layer resolution and diagnostics.
    /// </summary>
    protected ArchitectureEvaluatorResult CreateResult(
        string file,
        string? category = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        return new ArchitectureEvaluatorResult(
            Name,
            Path.GetFileName(file))
        {
            Category = category,

            Metrics = new Dictionary<string, double>(),

            Metadata = new Dictionary<string, string>
            {
                [FilePathMetadataKey] = file,
                [EvaluationScopeMetadataKey] = FileScope
            }
        };
    }

    /// <summary>
    /// Converts evaluator facts to legacy rule results.
    /// Compliance rule interpretation should normally be performed by
    /// RuleEngineCore instead.
    /// </summary>
    protected static IEnumerable<ArchitectureRuleresult> ConvertToRuleResults(
        IEnumerable<ArchitectureEvaluatorResult> evalResults,
        string ruleId)
    {
        ArgumentNullException.ThrowIfNull(evalResults);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        return evalResults.Select(result =>
        {
            var category = ArchitectureRuleCategory.General;

            if (!string.IsNullOrWhiteSpace(result.Category)
                && Enum.TryParse(
                    result.Category,
                    true,
                    out ArchitectureRuleCategory parsed))
            {
                category = parsed;
            }

            return new ArchitectureRuleresult
            {
                RuleId = ruleId,

                Message =
                    $"{result.Source}: {Path.GetFileName(result.Target)} " +
                    $"({string.Join(", ", result.Metrics.Select(metric =>
                        $"{metric.Key}={metric.Value:0.##}"))})",

                Category = category,
                Severity = ArchitectureRuleSeverity.Info,
                Target = result.Target
            };
        });
    }

    /// <summary>
    /// Enumerates files under backend/frontend application boundaries.
    /// This method intentionally preserves its existing behavior: callers
    /// needing extension or excluded-directory filtering should use
    /// ResolveSourceFiles instead.
    /// </summary>
    protected static IEnumerable<string> EnumerateApplicationFiles(
        string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        return Directory
            .EnumerateFiles(
                projectPath,
                "*.*",
                SearchOption.AllDirectories)
            .Where(file =>
            {
                var relative = Path.GetRelativePath(projectPath, file);

                return relative.StartsWith(
                           $"back{Path.DirectorySeparatorChar}",
                           StringComparison.OrdinalIgnoreCase)
                       || relative.StartsWith(
                           $"front{Path.DirectorySeparatorChar}",
                           StringComparison.OrdinalIgnoreCase);
            });
    }
}
