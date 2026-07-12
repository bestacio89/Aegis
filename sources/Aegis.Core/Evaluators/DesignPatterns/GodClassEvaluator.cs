using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;


/// <summary>
/// Detects and quantifies "God Class" anti-patterns by analyzing:
/// - Class size (lines)
/// - Method density
/// - Cohesion (property vs method ratio)
/// - Anemic domain tendencies
/// Outputs metrics and an overall GodClassScore (0–100).
/// </summary>
public sealed class GodClassEvaluator : BaseArchitectureEvaluator
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



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        if (!_policy.DetectGodClasses)
        {
            _logger.LogInformation(
                "💤 GodClassEvaluator disabled by policy.");

            return results;
        }



        var scaling =
            GetScalingFactor(
                Context?.Layer);



        var files =
            ResolveSourceFiles(
                projectPath,
                ".cs",
                ".java",
                ".ts",
                ".py");



        if (files.Count == 0)
        {
            _logger.LogInformation(
                "💀 No source files found for God Class evaluation.");

            return results;
        }



        _logger.LogTrace(
            "💀 Scanning {Count} files for God Class tendencies...",
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



            foreach (Match match in ClassRx.Matches(content))
            {
                var className =
                    match.Groups[1].Value;



                var methodCount =
                    MethodRx
                        .Matches(content)
                        .Count;



                var propertyCount =
                    PropertyRx
                        .Matches(content)
                        .Count;



                var lineCount =
                    content.Count(
                        c => c == '\n') + 1;



                var maxMethods =
                    (int)(
                        _policy.MaxMethodsPerClass *
                        scaling);



                var maxLines =
                    (int)(
                        _policy.MaxLinesPerClass *
                        scaling);



                var methodDensity =
                    methodCount /
                    (double)Math.Max(
                        1,
                        lineCount);



                var propertyRatio =
                    methodCount + propertyCount > 0
                        ?
                        propertyCount /
                        (double)(methodCount + propertyCount)
                        :
                        0;



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



                var godFactor =
                    ComputeGodFactor(
                        lineRatio,
                        methodRatio,
                        propertyRatio);



                var complianceScore =
                    Math.Round(
                        (1 - Math.Min(1, godFactor)) * 100,
                        2);



                results.Add(
                    new ArchitectureEvaluatorResult(
                        Name,
                        file)
                    {
                        Category =
                            nameof(
                                ArchitectureRuleCategory.DesignPatterns),

                        Metrics =
                        {
                            ["LineCount"] =
                                lineCount,

                            ["MethodCount"] =
                                methodCount,

                            ["PropertyCount"] =
                                propertyCount,

                            ["MethodDensity"] =
                                methodDensity,

                            ["LineRatio"] =
                                lineRatio,

                            ["MethodRatio"] =
                                methodRatio,

                            ["PropertyRatio"] =
                                propertyRatio,

                            ["GodClassSeverity"] =
                                godFactor,

                            ["GodClassComplianceScore"] =
                                complianceScore
                        },

                        Metadata =
                        {
                            ["ClassName"] =
                                className,

                            ["Language"] =
                                Context?.Language
                                ?? "Unknown",

                            ["Framework"] =
                                Context?.Framework
                                ?? "Unknown",

                            ["Layer"] =
                                Context?.Layer
                                ?? "Unknown",

                            ["ScalingFactor"] =
                                scaling.ToString("0.00"),

                            ["Policy_MaxLinesPerClass"] =
                                _policy.MaxLinesPerClass.ToString(),

                            ["Policy_MaxMethodsPerClass"] =
                                _policy.MaxMethodsPerClass.ToString()
                        }
                    });
            }
        }



        if (results.Count > 0)
        {
            var classResults =
                results.ToList();



            var averageCompliance =
                classResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "GodClassComplianceScore",
                            0));



            var averageSeverity =
                classResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "GodClassSeverity",
                            0));



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    projectPath)
                {
                    Category =
                        "DesignPatternSummary",

                    Metrics =
                    {
                        ["ClassCount"] =
                            classResults.Count,

                        ["AverageComplianceScore"] =
                            averageCompliance,

                        ["AverageLineCount"] =
                            classResults.Average(
                                r =>
                                    r.Metrics.GetValueOrDefault(
                                        "LineCount",
                                        0)),

                        ["AverageMethodCount"] =
                            classResults.Average(
                                r =>
                                    r.Metrics.GetValueOrDefault(
                                        "MethodCount",
                                        0)),

                        ["AverageGodClassSeverity"] =
                            averageSeverity,

                        ["OverallDesignHealth"] =
                            averageCompliance *
                            (
                                1 -
                                Math.Min(
                                    averageSeverity,
                                    1)
                            )
                    },

                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["PolicyEnabled"] =
                            _policy.DetectGodClasses.ToString()
                    }
                });
        }



        _logger.LogInformation(
            "💀 {Evaluator} completed with {Count} metric entries",
            Name,
            results.Count);



        return results;
    }



    private static double ComputeGodFactor(
        double lineRatio,
        double methodRatio,
        double propertyRatio)
    {
        var score =
            lineRatio * 0.4 +
            methodRatio * 0.4 +
            propertyRatio * 0.2;


        return Math.Min(
            score,
            2.0);
    }



    private double GetScalingFactor(
        string? layer)
    {
        if (string.IsNullOrWhiteSpace(layer))
        {
            return 1.0;
        }


        return _policy.LayerScaling.TryGetValue(
            layer,
            out var scale)
            ? scale
            : 1.0;
    }
}