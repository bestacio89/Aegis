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
/// 🧠 Base abstraction for all Aegis Evaluators.
/// Evaluators analyze source code and produce structured metrics or facts
/// consumed later by the RuleEngine.
/// </summary>
public abstract class BaseArchitectureEvaluator : IEvaluator, IScopedDependency
{
    protected readonly ILogger _logger;


    public abstract string Name { get; }


    public virtual string[] SupportedLanguages { get; } = ["*"];


    public virtual string[] SupportedFrameworks { get; } = ["*"];


    public bool IsEnabled { get; set; } = true;


    public double WeightFactor { get; set; } = 1.0;


    protected ProjectArchitectureContext? Context { get; private set; }



    protected BaseArchitectureEvaluator(
        ILogger logger)
    {
        _logger = logger;
    }



    public virtual void ApplyPolicy(
        AegisArchitecturePolicy policy)
    {
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



    public async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateAsync(
        string projectPath,
        ProjectArchitectureContext context,
        CancellationToken token = default)
    {
        Context = context;


        if (!IsEnabled)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Info,
                $"Evaluator disabled by policy: {Name}");

            return Enumerable.Empty<ArchitectureEvaluatorResult>();
        }


        try
        {
            var results =
                await EvaluateCoreAsync(
                    projectPath,
                    token);


            foreach (var result in results)
            {
                result.WeightFactor = WeightFactor;
                result.WasEnabled = true;
            }


            return results;
        }
        catch (Exception ex)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Error,
                "Evaluator failed.",
                ex);

#if DEBUG
            throw;
#else
            return Enumerable.Empty<ArchitectureEvaluatorResult>();
#endif
        }
    }



    protected abstract Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token);



    /// <summary>
    /// Centralized Aegis source filtering.
    /// Only analyzes application domains.
    /// </summary>
    protected IReadOnlyList<string> ResolveSourceFiles(
        string projectPath,
        params string[] extensions)
    {
        return Directory
            .EnumerateFiles(
                projectPath,
                "*.*",
                SearchOption.AllDirectories)
            .Where(file =>
                extensions.Any(ext =>
                    file.EndsWith(
                        ext,
                        StringComparison.OrdinalIgnoreCase)))
            .Where(file =>
                !IsExcludedDir(file))
            .Where(IsApplicationCode)
            .ToList();
    }



    /// <summary>
    /// Restricts analysis to backend/frontend application layers.
    /// Unity and external clients are intentionally ignored.
    /// </summary>
    private static bool IsApplicationCode(
        string file)
    {
        var normalized =
            file.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar);



        var ignored =
            new[]
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


        if (ignored.Any(x =>
                normalized.Contains(
                    x,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }



        var relativeParts =
            normalized.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries);



        return relativeParts.Any(part =>
            part.Equals(
                "back",
                StringComparison.OrdinalIgnoreCase)
            ||
            part.Equals(
                "front",
                StringComparison.OrdinalIgnoreCase));
    }



    protected static bool IsExcludedDir(
        string path)
        => PathUtils.IsExcludedDir(path);



    protected static IEnumerable<ArchitectureRuleresult>
        ConvertToRuleResults(
            IEnumerable<ArchitectureEvaluatorResult> evalResults,
            string ruleId)
    {
        return evalResults.Select(e =>
        {
            var category =
                ArchitectureRuleCategory.General;


            if (!string.IsNullOrWhiteSpace(e.Category) &&
                Enum.TryParse(
                    e.Category,
                    true,
                    out ArchitectureRuleCategory parsed))
            {
                category = parsed;
            }


            return new ArchitectureRuleresult
            {
                RuleId = ruleId,
                Message =
                    $"{e.Source}: {Path.GetFileName(e.Target)} " +
                    $"({string.Join(", ", e.Metrics.Select(m => $"{m.Key}={m.Value:0.##}"))})",

                Category = category,
                Severity = ArchitectureRuleSeverity.Info,
                Target = e.Target
            };
        });
    }


    /// <summary>
    /// Returns application source files restricted to Aegis application boundaries.
    /// Only /back and /front domains are analyzed.
    /// This avoids scanning external clients such as Unity projects.
    /// </summary>
  


    /// <summary>
    /// Filters files belonging only to supported application domains.
    /// </summary>
    protected static IEnumerable<string> EnumerateApplicationFiles(
     string projectPath)
    {
        return Directory
            .EnumerateFiles(
                projectPath,
                "*.*",
                SearchOption.AllDirectories)
            .Where(file =>
            {
                var relative =
                    Path.GetRelativePath(projectPath, file);

                return
                    relative.StartsWith(
                        $"back{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    relative.StartsWith(
                        $"front{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase);
            });
    }
}