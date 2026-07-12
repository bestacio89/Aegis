using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd;

/// <summary>
/// Quantitatively evaluates React (.jsx/.tsx/.js/.ts) components for:
/// - Hook discipline
/// - Component naming conventions
/// - Prop typing discipline
/// - Component complexity
/// - Legacy lifecycle usage
///
/// Produces ReactComplianceScore (0-100)
/// and project-level ReactHealthIndex.
/// </summary>
public sealed class ReactEvaluator : BaseArchitectureEvaluator
{
    private readonly FrontendPolicy _policy;


    public override string Name =>
        "ReactEvaluator";


    public override string[] SupportedLanguages =>
    [
        "TypeScript",
        "JavaScript"
    ];


    public override string[] SupportedFrameworks =>
    [
        "React"
    ];



    private static readonly Regex HookRx =
        new(
            @"\buse[A-Z]\w*\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex NonPascalComponentRx =
        new(
            @"(function|const)\s+[a-z]\w*",
            RegexOptions.Compiled);



    private static readonly Regex PropTypeRx =
        new(
            @"[Pp]rop[Tt]ypes\s*=",
            RegexOptions.Compiled);



    private static readonly Regex TsInterfaceRx =
        new(
            @"interface\s+[A-Z][A-Za-z0-9_]*\s*\{",
            RegexOptions.Compiled);



    private static readonly Regex ClassLifecycleRx =
        new(
            @"componentDid(Mount|Update|Unmount)",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    public ReactEvaluator(
        ILogger<ReactEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Frontend
            ?? new FrontendPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();


        var files =
            ResolveReactFiles(projectPath);



        if (files.Count == 0)
        {
            _logger.LogInformation(
                "⚛️ No React files found for evaluation.");

            return results;
        }



        _logger.LogInformation(
            "⚛️ Running {Evaluator} on {Count} files",
            Name,
            files.Count);



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



            var fileName =
                Path.GetFileName(file);



            var isJavaScript =
                file.EndsWith(
                    ".js",
                    StringComparison.OrdinalIgnoreCase)
                ||
                file.EndsWith(
                    ".jsx",
                    StringComparison.OrdinalIgnoreCase);



            var hasProps =
                content.Contains(
                    "props",
                    StringComparison.OrdinalIgnoreCase);



            var hookCount =
                HookRx.Matches(content).Count;



            var lineCount =
                content.Split('\n').Length;



            var hookScore =
                CalculateHookScore(
                    content,
                    hookCount);



            var namingScore =
                CalculateNamingScore(
                    content);



            var typingScore =
                CalculateTypingScore(
                    content,
                    isJavaScript,
                    hasProps);



            var complexityScore =
                CalculateComplexityScore(
                    lineCount);



            var modernizationScore =
                ClassLifecycleRx.IsMatch(content)
                    ? 0.5
                    : 1.0;



            var complianceScore =
                ComputeCompliance(
                    hookScore,
                    namingScore,
                    typingScore,
                    complexityScore,
                    modernizationScore);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    Category =
                        "Frontend",

                    Metrics =
                    {
                        ["HookDisciplineScore"] =
                            hookScore,

                        ["NamingScore"] =
                            namingScore,

                        ["TypingScore"] =
                            typingScore,

                        ["ComplexityScore"] =
                            complexityScore,

                        ["ModernizationScore"] =
                            modernizationScore,

                        ["HookCount"] =
                            hookCount,

                        ["LineCount"] =
                            lineCount,

                        ["ReactComplianceScore"] =
                            complianceScore
                    },

                    Metadata =
                    {
                        ["FileName"] =
                            fileName,

                        ["Language"] =
                            Context?.Language
                            ?? "Unknown",

                        ["Framework"] =
                            Context?.Framework
                            ?? "Unknown",

                        ["Layer"] =
                            Context?.Layer
                            ?? "Unknown",

                        ["Target"] =
                            file,

                        ["IsJavaScript"] =
                            isJavaScript.ToString(),

                        ["HasProps"] =
                            hasProps.ToString()
                    }
                });
        }



        AddSummary(
            results,
            projectPath);



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} metric entries",
            Name,
            results.Count);



        return results;
    }



    private static List<string> ResolveReactFiles(
        string projectPath)
    {
        return Directory
            .EnumerateFiles(
                projectPath,
                "*.*",
                SearchOption.AllDirectories)
            .Where(
                f =>
                    f.EndsWith(
                        ".jsx",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    f.EndsWith(
                        ".tsx",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    f.EndsWith(
                        ".js",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    f.EndsWith(
                        ".ts",
                        StringComparison.OrdinalIgnoreCase))
            .Where(
                f =>
                    !IsExcludedDir(f))
            .ToList();
    }



    private double CalculateHookScore(
        string content,
        int hookCount)
    {
        double score = 1.0;


        if (_policy.CheckHooksRules &&
            HookRx.IsMatch(content) &&
            content.Contains(
                "if (",
                StringComparison.Ordinal))
        {
            score = 0;
        }


        if (_policy.MaxHooksPerComponent > 0 &&
            hookCount > _policy.MaxHooksPerComponent)
        {
            score *= Math.Max(
                0.5,
                1 -
                hookCount /
                (double)(_policy.MaxHooksPerComponent * 2));
        }


        return score;
    }



    private double CalculateNamingScore(
        string content)
    {
        if (_policy.EnforceComponentPascalCase &&
            NonPascalComponentRx.IsMatch(content))
        {
            return 0.5;
        }


        return 1.0;
    }



    private static double CalculateTypingScore(
        string content,
        bool isJavaScript,
        bool hasProps)
    {
        if (!hasProps)
        {
            return 1.0;
        }


        if (isJavaScript &&
            !PropTypeRx.IsMatch(content))
        {
            return 0.5;
        }


        if (!isJavaScript &&
            !TsInterfaceRx.IsMatch(content))
        {
            return 0.5;
        }


        return 1.0;
    }



    private double CalculateComplexityScore(
        int lineCount)
    {
        if (_policy.MaxComponentComplexity <= 0 ||
            lineCount <= _policy.MaxComponentComplexity)
        {
            return 1.0;
        }


        return Math.Max(
            0,
            1 -
            (double)lineCount /
            (_policy.MaxComponentComplexity * 2));
    }



    private void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        if (results.Count == 0)
        {
            return;
        }


        var avgCompliance =
            results.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "ReactComplianceScore",
                        0));


        var avgHook =
            results.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "HookDisciplineScore",
                        0));


        var avgTyping =
            results.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "TypingScore",
                        0));


        var avgComplexity =
            results.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "ComplexityScore",
                        0));



        results.Add(
            new ArchitectureEvaluatorResult(
                Name,
                projectPath)
            {
                Category =
                    "FrontendSummary",

                Metrics =
                {
                    ["ReactFileCount"] =
                        results.Count,

                    ["AverageComplianceScore"] =
                        avgCompliance,

                    ["AverageHookDiscipline"] =
                        avgHook,

                    ["AverageTyping"] =
                        avgTyping,

                    ["AverageComplexity"] =
                        avgComplexity,

                    ["ReactHealthIndex"] =
                        avgCompliance * 0.6 +
                        (avgHook * 100) * 0.4
                },

                Metadata =
                {
                    ["Evaluator"] =
                        Name,

                    ["Language"] =
                        Context?.Language
                        ?? "Unknown",

                    ["Framework"] =
                        Context?.Framework
                        ?? "Unknown",

                    ["Layer"] =
                        Context?.Layer
                        ?? "Unknown",

                    ["PolicyEnabled"] =
                        _policy.CheckHooksRules.ToString()
                }
            });
    }



    private static double ComputeCompliance(
        double hookDiscipline,
        double naming,
        double typing,
        double complexity,
        double modernization)
    {
        var score =
            hookDiscipline * 0.30 +
            naming * 0.15 +
            typing * 0.20 +
            complexity * 0.20 +
            modernization * 0.15;


        return Math.Round(
            score * 100,
            2);
    }
}