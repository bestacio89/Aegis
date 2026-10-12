using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd;


/// <summary>
/// Evaluates Blazor components for architectural discipline:
///
/// - Component boundaries
/// - Code-behind separation
/// - Dependency injection discipline
/// - Lifecycle usage
/// - Event handling practices
/// - Component composition
/// - Rendering architecture
///
/// Produces:
/// - BlazorComplianceScore (0-100)
/// - BlazorHealthIndex
/// - Component-level architecture metrics
/// </summary>
public sealed class BlazorEvaluator : BaseArchitectureEvaluator
{
    private readonly FrontendPolicy _policy;


    public override string Name =>
        nameof(BlazorEvaluator);


    public override string[] SupportedLanguages =>
    [
        "Razor",
        "C#"
    ];


    public override string[] SupportedFrameworks =>
    [
        "Blazor"
    ];



    private static readonly Regex ComponentRx =
        new(
            @"@\s*(page|inherits|rendermode)\b",
            RegexOptions.Compiled |
            RegexOptions.IgnoreCase);



    private static readonly Regex InlineCodeRx =
        new(
            @"@\s*code\s*\{",
            RegexOptions.Compiled);



    private static readonly Regex InjectRx =
        new(
            @"@\s*inject\s+\w+",
            RegexOptions.Compiled);



    private static readonly Regex ManualInstantiationRx =
        new(
            @"\bnew\s+\w+(Service|Repository|Manager)\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex LifecycleRx =
        new(
            @"\b(OnInitialized|OnInitializedAsync|OnParametersSet|OnParametersSetAsync|OnAfterRender|OnAfterRenderAsync|Dispose|DisposeAsync)\b",
            RegexOptions.Compiled);



    private static readonly Regex AsyncVoidRx =
        new(
            @"async\s+void\s+\w+\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex RenderModeRx =
        new(
            @"@\s*rendermode\s+\w+",
            RegexOptions.Compiled);



    private static readonly Regex EventHandlerRx =
        new(
            @"@\w+\s*=\s*""",
            RegexOptions.Compiled);



    private static readonly Regex ChildComponentRx =
        new(
            @"<[A-Z][A-Za-z0-9]+",
            RegexOptions.Compiled);



    public BlazorEvaluator(
        ILogger<BlazorEvaluator> logger,
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
            ResolveSourceFiles(
                projectPath,
                ".razor");


        if (files.Count == 0)
        {
            return results;
        }



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



            if (!ComponentRx.IsMatch(content))
            {
                continue;
            }



            var metrics =
                AnalyzeComponent(
                    file,
                    content);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    Category =
                        "Frontend",


                    Metrics =
                    {
                        ["ComplexityScore"] =
                            metrics.ComplexityScore,

                        ["CodeBehindScore"] =
                            metrics.CodeBehindScore,

                        ["DependencyScore"] =
                            metrics.DependencyScore,

                        ["CompositionScore"] =
                            metrics.CompositionScore,

                        ["LifecycleScore"] =
                            metrics.LifecycleScore,

                        ["EventHandlingScore"] =
                            metrics.EventScore,

                        ["BlazorComplianceScore"] =
                            metrics.ComplianceScore
                    },


                    Metadata =
                    {
                        ["FilePath"] =
                            file,

                        ["FileName"] =
                            Path.GetFileName(file),

                        ["Framework"] =
                            "Blazor",

                        ["Language"] =
                            "Razor",

                        ["LineCount"] =
                            metrics.LineCount.ToString(),

                        ["InjectionCount"] =
                            metrics.InjectionCount.ToString(),

                        ["ChildComponents"] =
                            metrics.ChildComponentCount.ToString(),

                        ["HasCodeBehind"] =
                            metrics.HasCodeBehind.ToString(),

                        ["HasInlineCode"] =
                            metrics.HasInlineCode.ToString()
                    }
                });
        }



        if (results.Count > 0)
        {
            AddSummaryResult(
                results,
                projectPath);
        }



        return results;
    }




    private ComponentMetrics AnalyzeComponent(
        string file,
        string content)
    {
        var lineCount =
            content.Count(
                c => c == '\n') + 1;



        var hasInlineCode =
            InlineCodeRx.IsMatch(content);



        var hasCodeBehind =
            File.Exists(
                file + ".cs");



        var injectionCount =
            InjectRx.Matches(content)
                .Count;



        var childComponents =
            ChildComponentRx.Matches(content)
                .Count;



        var hasLifecycle =
            LifecycleRx.IsMatch(content);



        var hasAsyncVoid =
            AsyncVoidRx.IsMatch(content);



        var eventCount =
            EventHandlerRx.Matches(content)
                .Count;



        var hasManualInstantiation =
            ManualInstantiationRx.IsMatch(content);



        var complexity =
            CalculateComplexity(
                lineCount);



        return new ComponentMetrics
        {
            LineCount = lineCount,

            HasInlineCode = hasInlineCode,

            HasCodeBehind = hasCodeBehind,

            InjectionCount = injectionCount,

            ChildComponentCount = childComponents,


            ComplexityScore =
                complexity,


            CodeBehindScore =
                hasCodeBehind
                    ? 1
                    : hasInlineCode
                        ? 0.25
                        : 0.8,


            DependencyScore =
                hasManualInstantiation
                    ? 0
                    : CalculateDependencyScore(
                        injectionCount),


            CompositionScore =
                CalculateCompositionScore(
                    childComponents),


            LifecycleScore =
                hasLifecycle
                    ? 1
                    : 0.8,


            EventScore =
                hasAsyncVoid
                    ? 0.2
                    : CalculateEventScore(
                        eventCount),


            ComplianceScore =
                ComputeCompliance(
                    complexity,
                    hasCodeBehind,
                    injectionCount,
                    childComponents,
                    hasLifecycle,
                    eventCount,
                    hasAsyncVoid)
        };
    }



    private double CalculateComplexity(
        int lines)
    {
        var limit =
            _policy.MaxComponentComplexity;


        if (limit <= 0 ||
            lines <= limit)
        {
            return 1;
        }


        if (lines <= limit * 1.5)
            return 0.75;


        if (lines <= limit * 2)
            return 0.5;


        return 0;
    }



    private static double CalculateDependencyScore(
        int count)
    {
        return count switch
        {
            <= 3 => 1,
            <= 5 => 0.7,
            <= 8 => 0.4,
            _ => 0.1
        };
    }



    private static double CalculateCompositionScore(
        int count)
    {
        return count switch
        {
            >= 5 => 1,
            >= 2 => 0.8,
            1 => 0.6,
            _ => 0.4
        };
    }



    private static double CalculateEventScore(
        int count)
    {
        return count switch
        {
            <= 3 => 1,
            <= 6 => 0.6,
            _ => 0.3
        };
    }



    private static double ComputeCompliance(
        double complexity,
        bool codeBehind,
        int injections,
        int children,
        bool lifecycle,
        int events,
        bool asyncVoid)
    {
        var score =
            complexity * 0.25 +

            (codeBehind ? 1 : 0.3) * 0.25 +

            CalculateDependencyScore(
                injections) * 0.20 +

            CalculateCompositionScore(
                children) * 0.15 +

            (asyncVoid ? 0.2 : 1) * 0.10 +

            (events > 6 ? 0.5 : 1) * 0.05;



        return Math.Round(
            score * 100,
            2);
    }



    private void AddSummaryResult(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var average =
            results.Average(
                x =>
                    x.Metrics.GetValueOrDefault(
                        "BlazorComplianceScore",
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
                    ["BlazorComponentCount"] =
                        results.Count,

                    ["AverageComplianceScore"] =
                        average,

                    ["BlazorHealthIndex"] =
                        average
                },

                Metadata =
                {
                    ["Framework"] =
                        "Blazor"
                }
            });
    }



    private sealed class ComponentMetrics
    {
        public int LineCount { get; init; }

        public bool HasInlineCode { get; init; }

        public bool HasCodeBehind { get; init; }

        public int InjectionCount { get; init; }

        public int ChildComponentCount { get; init; }

        public double ComplexityScore { get; init; }

        public double CodeBehindScore { get; init; }

        public double DependencyScore { get; init; }

        public double CompositionScore { get; init; }

        public double LifecycleScore { get; init; }

        public double EventScore { get; init; }

        public double ComplianceScore { get; init; }
    }
}