using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd;


/// <summary>
/// Quantitatively evaluates Blazor (.razor) components for:
/// - Component boundaries
/// - Code-behind discipline
/// - Dependency injection usage
/// - Lifecycle management
/// - Event handling practices
/// - Rendering architecture
///
/// Produces BlazorComplianceScore (0-100)
/// and project-wide BlazorHealthIndex.
/// </summary>
public sealed class BlazorEvaluator : BaseArchitectureEvaluator
{
    private readonly FrontendPolicy _policy;


    public override string Name =>
        "BlazorEvaluator";


    public override string[] SupportedLanguages =>
    [
        "Razor",
        "C#"
    ];


    public override string[] SupportedFrameworks =>
    [
        "Blazor",
        "ASP.NET Core"
    ];



    private static readonly Regex ComponentRx =
        new(
            @"@\s*page\s+""[^""]+""|@\s*code\s*\{|@\s*inherits\s+\w+",
            RegexOptions.Compiled);



    private static readonly Regex InlineCodeRx =
        new(
            @"@\s*code\s*\{",
            RegexOptions.Compiled);



    private static readonly Regex InjectRx =
        new(
            @"@\s*inject\s+\w+\s+\w+",
            RegexOptions.Compiled);



    private static readonly Regex ManualInstantiationRx =
        new(
            @"\bnew\s+\w+(Service|Repository|Manager)\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex LifecycleRx =
        new(
            @"\b(OnInitialized|OnInitializedAsync|OnParametersSet|OnParametersSetAsync|OnAfterRender|OnAfterRenderAsync|Dispose|DisposeAsync)\b",
            RegexOptions.Compiled);



    private static readonly Regex EventCallbackRx =
        new(
            @"EventCallback(?:<|>)?",
            RegexOptions.Compiled);



    private static readonly Regex AsyncVoidRx =
        new(
            @"async\s+void\s+\w+\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex RenderModeRx =
        new(
            @"@rendermode\s+\w+",
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



        _logger.LogInformation(
            "🟣 Running {Evaluator} on {Count} files",
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



            if (!ComponentRx.IsMatch(content))
            {
                continue;
            }



            var metrics =
                AnalyzeComponent(
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

                        ["DependencyInjectionScore"] =
                            metrics.DependencyScore,

                        ["LifecycleScore"] =
                            metrics.LifecycleScore,

                        ["EventHandlingScore"] =
                            metrics.EventScore,

                        ["RenderModeScore"] =
                            metrics.RenderModeScore,

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
                            Context?.Framework
                            ?? "Blazor",

                        ["Language"] =
                            Context?.Language
                            ?? "Razor",

                        ["LineCount"] =
                            metrics.LineCount.ToString(),

                        ["HasInlineCode"] =
                            metrics.HasInlineCode.ToString(),

                        ["HasInjection"] =
                            metrics.HasInjection.ToString(),

                        ["HasLifecycle"] =
                            metrics.HasLifecycle.ToString(),

                        ["HasRenderMode"] =
                            metrics.HasRenderMode.ToString()
                    }
                });
        }



        if (results.Count > 0)
        {
            AddSummaryResult(
                results,
                projectPath);
        }



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} results",
            Name,
            results.Count);



        return results;
    }



    private ComponentMetrics AnalyzeComponent(
        string content)
    {
        var lineCount =
            content.Count(
                c => c == '\n') + 1;



        var hasInlineCode =
            InlineCodeRx.IsMatch(content);


        var hasInjection =
            InjectRx.IsMatch(content);


        var hasManualInstantiation =
            ManualInstantiationRx.IsMatch(content);


        var hasLifecycle =
            LifecycleRx.IsMatch(content);


        var hasEventCallback =
            EventCallbackRx.IsMatch(content);


        var hasAsyncVoid =
            AsyncVoidRx.IsMatch(content);


        var hasRenderMode =
            RenderModeRx.IsMatch(content);



        var complexityScore =
            CalculateComplexity(
                lineCount);



        var codeBehindScore =
            hasInlineCode
                ? 0.5
                : 1.0;



        var dependencyScore =
            hasManualInstantiation
                ? 0
                : 1;



        var lifecycleScore =
            hasLifecycle
                ? 1
                : 0.8;



        var eventScore =
            hasAsyncVoid
                ? 0.3
                : hasEventCallback
                    ? 1
                    : 0.8;



        var renderModeScore =
            hasRenderMode
                ? 1
                : 0.9;



        return new ComponentMetrics
        {
            LineCount = lineCount,

            HasInlineCode = hasInlineCode,

            HasInjection = hasInjection,

            HasLifecycle = hasLifecycle,

            HasRenderMode = hasRenderMode,

            ComplexityScore = complexityScore,

            CodeBehindScore = codeBehindScore,

            DependencyScore = dependencyScore,

            LifecycleScore = lifecycleScore,

            EventScore = eventScore,

            RenderModeScore = renderModeScore,

            ComplianceScore =
                ComputeCompliance(
                    complexityScore,
                    codeBehindScore,
                    dependencyScore,
                    lifecycleScore,
                    eventScore,
                    renderModeScore)
        };
    }



    private double CalculateComplexity(
        int lineCount)
    {
        if (_policy.MaxComponentComplexity <= 0)
        {
            return 1;
        }


        if (lineCount <= _policy.MaxComponentComplexity)
        {
            return 1;
        }


        return Math.Max(
            0,
            1 -
            (double)lineCount /
            (_policy.MaxComponentComplexity * 2));
    }



    private void AddSummaryResult(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var componentResults =
            results.ToList();



        var averageCompliance =
            componentResults.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "BlazorComplianceScore",
                        0));



        var averageComplexity =
            componentResults.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "ComplexityScore",
                        0));



        var averageDependency =
            componentResults.Average(
                r =>
                    r.Metrics.GetValueOrDefault(
                        "DependencyInjectionScore",
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
                        componentResults.Count,

                    ["AverageComplianceScore"] =
                        averageCompliance,

                    ["AverageComplexityScore"] =
                        averageComplexity,

                    ["AverageDependencyScore"] =
                        averageDependency,

                    ["BlazorHealthIndex"] =
                        averageCompliance *
                        0.6 +
                        averageDependency *
                        100 *
                        0.4
                },

                Metadata =
                {
                    ["Evaluator"] =
                        Name,

                    ["Framework"] =
                        Context?.Framework
                        ?? "Blazor",

                    ["PolicyEnabled"] =
                        "True"
                }
            });
    }



    private static double ComputeCompliance(
        double complexity,
        double codeBehind,
        double dependency,
        double lifecycle,
        double events,
        double renderMode)
    {
        var score =
            complexity * 0.20 +
            codeBehind * 0.20 +
            dependency * 0.20 +
            lifecycle * 0.15 +
            events * 0.15 +
            renderMode * 0.10;


        return Math.Round(
            score * 100,
            2);
    }



    private sealed class ComponentMetrics
    {
        public int LineCount { get; init; }

        public bool HasInlineCode { get; init; }

        public bool HasInjection { get; init; }

        public bool HasLifecycle { get; init; }

        public bool HasRenderMode { get; init; }

        public double ComplexityScore { get; init; }

        public double CodeBehindScore { get; init; }

        public double DependencyScore { get; init; }

        public double LifecycleScore { get; init; }

        public double EventScore { get; init; }

        public double RenderModeScore { get; init; }

        public double ComplianceScore { get; init; }
    }
}