using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd;

/// <summary>
/// Quantitatively evaluates Blazor (.razor) components for
/// component boundaries, lifecycle discipline, dependency injection,
/// state management, and rendering architecture.
///
/// Produces BlazorComplianceScore (0-100)
/// and project-wide BlazorHealthIndex.
/// </summary>
public sealed class BlazorEvaluator : BaseArchitectureEvaluator
{
    private readonly FrontendPolicy _policy;

    public override string Name => "BlazorEvaluator";

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
        new(@"@\s*page\s+""[^""]+""|@code\s*\{|@inherits\s+\w+",
            RegexOptions.Compiled);

    private static readonly Regex InlineCodeRx =
        new(@"@code\s*\{",
            RegexOptions.Compiled);

    private static readonly Regex InjectRx =
        new(@"@\s*inject\s+\w+\s+\w+",
            RegexOptions.Compiled);

    private static readonly Regex ManualInstantiationRx =
        new(@"\bnew\s+\w+(Service|Repository|Manager)\s*\(",
            RegexOptions.Compiled);

    private static readonly Regex LifecycleRx =
        new(@"\b(OnInitialized|OnInitializedAsync|OnParametersSet|OnParametersSetAsync|OnAfterRender|OnAfterRenderAsync|Dispose|DisposeAsync)\b",
            RegexOptions.Compiled);

    private static readonly Regex EventCallbackRx =
        new(@"EventCallback|EventCallback<",
            RegexOptions.Compiled);

    private static readonly Regex AsyncVoidRx =
        new(@"async\s+void\s+\w+\s*\(",
            RegexOptions.Compiled);

    private static readonly Regex RenderModeRx =
        new(@"@rendermode\s+\w+",
            RegexOptions.Compiled);


    public BlazorEvaluator(
        ILogger<BlazorEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Frontend ?? new FrontendPolicy();
    }


    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        var files = Directory.EnumerateFiles(
                projectPath,
                "*.razor",
                SearchOption.AllDirectories)
            .Where(f => !IsExcludedDir(f))
            .ToList();


        _logger.LogInformation(
            "🟣 Running {Evaluator} on {Count} Blazor components",
            Name,
            files.Count);


        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(file, token);

            var lineCount = content.Split('\n').Length;


            bool isComponent = ComponentRx.IsMatch(content);
            bool hasInlineCode = InlineCodeRx.IsMatch(content);
            bool hasInjection = InjectRx.IsMatch(content);
            bool hasManualConstruction = ManualInstantiationRx.IsMatch(content);
            bool hasLifecycle = LifecycleRx.IsMatch(content);
            bool hasEventCallback = EventCallbackRx.IsMatch(content);
            bool hasAsyncVoid = AsyncVoidRx.IsMatch(content);
            bool hasRenderMode = RenderModeRx.IsMatch(content);


            if (!isComponent)
                continue;


            // Component size discipline
            double complexityScore = 1.0;

            if (_policy.MaxComponentComplexity > 0 &&
                lineCount > _policy.MaxComponentComplexity)
            {
                complexityScore =
                    Math.Max(
                        0,
                        1 -
                        (double)lineCount /
                        (_policy.MaxComponentComplexity * 2));
            }


            // Inline code penalty
            double codeBehindScore =
                hasInlineCode
                    ? 0.5
                    : 1.0;


            // DI discipline
            double dependencyScore =
                hasManualConstruction
                    ? 0.0
                    : 1.0;


            // Lifecycle discipline
            double lifecycleScore =
                hasLifecycle
                    ? 1.0
                    : 0.8;


            // Event handling
            double eventScore =
                hasAsyncVoid
                    ? 0.3
                    : hasEventCallback
                        ? 1.0
                        : 0.8;


            // Rendering awareness
            double renderModeScore =
                hasRenderMode
                    ? 1.0
                    : 0.9;


            double complianceScore =
                ComputeCompliance(
                    complexityScore,
                    codeBehindScore,
                    dependencyScore,
                    lifecycleScore,
                    eventScore,
                    renderModeScore);


            results.Add(new ArchitectureEvaluatorResult(Name, file)
            {
                Category = "Frontend",

                Metrics = new Dictionary<string, double>
                {
                    ["ComplexityScore"] = complexityScore,
                    ["CodeBehindScore"] = codeBehindScore,
                    ["DependencyInjectionScore"] = dependencyScore,
                    ["LifecycleScore"] = lifecycleScore,
                    ["EventHandlingScore"] = eventScore,
                    ["RenderModeScore"] = renderModeScore,
                    ["BlazorComplianceScore"] = complianceScore
                },

                Metadata = new Dictionary<string, string>
                {
                    ["FileName"] = Path.GetFileName(file),
                    ["Framework"] = "Blazor",
                    ["Language"] = "Razor",
                    ["LineCount"] = lineCount.ToString(),
                    ["HasInlineCode"] = hasInlineCode.ToString(),
                    ["HasInjection"] = hasInjection.ToString(),
                    ["HasLifecycle"] = hasLifecycle.ToString(),
                    ["HasRenderMode"] = hasRenderMode.ToString()
                }
            });
        }


        if (results.Count > 0)
        {
            var avgScore =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "BlazorComplianceScore",
                        0));


            var avgComplexity =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "ComplexityScore",
                        0));


            var avgDI =
                results.Average(
                    r => r.Metrics.GetValueOrDefault(
                        "DependencyInjectionScore",
                        0));


            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "FrontendSummary",

                Metrics = new Dictionary<string, double>
                {
                    ["BlazorComponentCount"] = results.Count,
                    ["AverageComplianceScore"] = avgScore,
                    ["AverageComplexityScore"] = avgComplexity,
                    ["AverageDependencyScore"] = avgDI,

                    ["BlazorHealthIndex"] =
                        avgScore * 0.6 +
                        avgDI * 100 * 0.4
                },

                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["Framework"] = "Blazor"
                }
            });
        }


        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} results",
            Name,
            results.Count);


        return results;
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


        return Math.Round(score * 100, 2);
    }
}