using System.Text.RegularExpressions;

using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;



/// <summary>
/// Detects God Class tendencies.
///
/// Produces structural metrics:
/// - class size
/// - method density
/// - property/method distribution
/// - complexity indicators
///
/// RuleEngine determines severity and remediation.
/// </summary>
public sealed class GodClassEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;



    public override string Name =>
        "GodClassEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "TypeScript"
    ];



    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring",
        "NestJS",
        "FastAPI"
    ];



    private static readonly Regex ClassRx =
        new(
            @"class\s+(\w+)\b",
            RegexOptions.Compiled);



    private static readonly Regex MethodRx =
        new(
            @"\b(public|private|protected|def|function)\s+\w+\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex PropertyRx =
        new(
            @"\b(get|set|var|val|let|this\.)\w+",
            RegexOptions.Compiled);



    public GodClassEvaluator(
        ILogger<GodClassEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.DesignPatterns
            ?? new DesignPatternPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        if (Context is null)
            return results;



        if (!_policy.DetectGodClasses)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "God class evaluation disabled.");

            return results;
        }



        var files =
            ResolveSourceFiles(
                projectPath,
                ".cs",
                ".java",
                ".ts",
                ".py");



        if (files.Count == 0)
            return results;



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
            catch (Exception ex)
            {
                AegisDiagnostics.Report(
                    Name,
                    DiagnosticLevel.Warning,
                    $"Unable to read {file}.",
                    ex);

                continue;
            }



            foreach (Match match in ClassRx.Matches(content))
            {
                var className =
                    match.Groups[1].Value;


                var layer =
                    ResolveLayer(file);



                var scaling =
                    GetScalingFactor(
                        layer);



                var methodCount =
                    MethodRx.Matches(content)
                    .Count;



                var propertyCount =
                    PropertyRx.Matches(content)
                    .Count;



                var lineCount =
                    content.Count(
                        c =>
                            c == '\n')
                    + 1;



                var maxMethods =
                    (int)
                    (
                        _policy.MaxMethodsPerClass *
                        scaling
                    );



                var maxLines =
                    (int)
                    (
                        _policy.MaxLinesPerClass *
                        scaling
                    );



                var lineRatio =
                    lineCount /
                    (double)Math.Max(
                        1,
                        maxLines);



                var methodRatio =
                    methodCount /
                    (double)Math.Max(
                        1,
                        maxMethods);



                var propertyRatio =
                    methodCount + propertyCount == 0
                        ? 0
                        :
                        propertyCount /
                        (double)
                        (
                            methodCount +
                            propertyCount
                        );



                var severity =
                    ComputeGodFactor(
                        lineRatio,
                        methodRatio,
                        propertyRatio);



                results.Add(
                    CreateResult(
                        file,
                        className,
                        layer,
                        lineCount,
                        methodCount,
                        propertyCount,
                        severity,
                        methodCount /
                            (double)Math.Max(
                                1,
                                lineCount),
                        propertyRatio));
            }
        }



        if (results.Count > 0)
        {
            results.Add(
                CreateSummary(
                    projectPath,
                    results));
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"God class evaluation completed with {results.Count} entries.");



        return results;
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        string className,
        string layer,
        int lines,
        int methods,
        int properties,
        double severity,
        double density,
        double propertyRatio)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            ProjectName =
                Context!.ProjectName,

            Language =
                Context.Language,

            Framework =
                Context.Framework,


            Layer =
                layer,


            DetectionConfidence =
                Context.Confidence,


            Category =
                "DesignPattern",


            Metrics =
            {
                ["LineCount"] =
                    lines,

                ["MethodCount"] =
                    methods,

                ["PropertyCount"] =
                    properties,

                ["MethodDensity"] =
                    density,

                ["PropertyRatio"] =
                    propertyRatio,

                ["GodClassSeverity"] =
                    severity,

                ["GodClassComplianceScore"] =
                    Math.Round(
                        (1 -
                         Math.Min(
                             severity,
                             1))
                        * 100,
                        2)
            },


            Metadata =
            {
                ["ClassName"] =
                    className,

                ["Language"] =
                    Context.Language,

                ["Framework"] =
                    Context.Framework
                    ?? "Unknown",

                ["Layer"] =
                    layer
            }
        };
    }



    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        IEnumerable<ArchitectureEvaluatorResult> entries)
    {
        var results =
            entries.ToList();



        return new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            ProjectName =
                Context?.ProjectName ?? string.Empty,


            Category =
                "DesignPatternSummary",


            Metrics =
            {
                ["ClassCount"] =
                    results.Count,

                ["AverageComplianceScore"] =
                    results.Average(
                        x =>
                            x.Metrics.GetValueOrDefault(
                                "GodClassComplianceScore")),


                ["AverageSeverity"] =
                    results.Average(
                        x =>
                            x.Metrics.GetValueOrDefault(
                                "GodClassSeverity"))
            },


            Metadata =
            {
                ["Evaluator"] =
                    Name
            }
        };
    }



    private string ResolveLayer(
        string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(
                layer =>
                    layer.Files.Contains(
                        file,
                        StringComparer.OrdinalIgnoreCase))
            ?.Name
            ??
            "Unknown";
    }



    private double GetScalingFactor(
        string layer)
    {
        return _policy.LayerScaling.TryGetValue(
            layer,
            out var scale)
            ? scale
            : 1.0;
    }



    private static double ComputeGodFactor(
        double lineRatio,
        double methodRatio,
        double propertyRatio)
    {
        return Math.Min(
            lineRatio * 0.4 +
            methodRatio * 0.4 +
            propertyRatio * 0.2,
            2.0);
    }
}