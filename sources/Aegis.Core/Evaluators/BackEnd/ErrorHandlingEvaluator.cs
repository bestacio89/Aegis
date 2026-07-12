using System.Text.RegularExpressions;
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.BackEnd;

public sealed class ErrorHandlingEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly ErrorHandlingPolicy _policy;



    public override string Name =>
        "ErrorHandlingEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python"
    ];



    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring Boot",
        "FastAPI"
    ];



    private static readonly Regex CatchRegex =
        new(
            @"catch\s*(?:\(\s*(?<type>[A-Za-z0-9_.]+)(?:\s+\w+)?\s*\))?\s*\{(?<body>.*?)\}",
            RegexOptions.Singleline |
            RegexOptions.Compiled);



    public ErrorHandlingEvaluator(
        ILogger<ErrorHandlingEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.ErrorHandling
            ?? new ErrorHandlingPolicy();
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



        var files =
            ResolveSourceFiles(
                projectPath,
                GetExtensions(Context.Language));



        if (files.Count == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "No source files found for error handling analysis.");

            return results;
        }



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();



            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



            var metrics =
                AnalyzeCatchBlocks(
                    content);



            if (metrics.TotalCatchBlocks == 0)
                continue;



            results.Add(
                CreateResult(
                    file,
                    metrics));
        }



        AddSummary(
            results,
            projectPath);



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Error handling analysis completed with {results.Count} results.");



        return results;
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        (
            int TotalCatchBlocks,
            int EmptyCatchBlocks,
            int GenericCatchBlocks,
            int SwallowedExceptions) metrics)
    {
        var layer =
            ResolveLayer(file);



        var result =
            CreateResult(
                file,
                "ErrorHandling");



        result.ProjectName =
            Context?.ProjectName;

        result.Language =
            Context?.Language;

        result.Framework =
            Context?.Framework;

        result.Layer =
            layer;

        result.DetectionConfidence =
            Context?.Confidence ?? 0;



        result.Metrics["TotalCatchBlocks"] =
            metrics.TotalCatchBlocks;

        result.Metrics["EmptyCatchBlocks"] =
            metrics.EmptyCatchBlocks;

        result.Metrics["GenericCatchBlocks"] =
            metrics.GenericCatchBlocks;

        result.Metrics["SwallowedExceptions"] =
            metrics.SwallowedExceptions;



        result.Metadata["Language"] =
            Context?.Language ?? "Unknown";

        result.Metadata["Framework"] =
            Context?.Framework ?? "Unknown";

        result.Metadata["Layer"] =
            layer ?? "Unknown";



        return result;
    }



    private static (
        int TotalCatchBlocks,
        int EmptyCatchBlocks,
        int GenericCatchBlocks,
        int SwallowedExceptions)
        AnalyzeCatchBlocks(
            string content)
    {
        int total = 0;
        int empty = 0;
        int generic = 0;
        int swallowed = 0;



        foreach (Match match in CatchRegex.Matches(content))
        {
            total++;



            var exceptionType =
                match.Groups["type"]
                    .Value;



            var body =
                match.Groups["body"]
                    .Value;



            if (string.IsNullOrWhiteSpace(body))
            {
                empty++;
            }



            if (exceptionType.Equals(
                    "Exception",
                    StringComparison.OrdinalIgnoreCase))
            {
                generic++;
            }



            if (!ContainsHandling(body))
            {
                swallowed++;
            }
        }



        return
        (
            total,
            empty,
            generic,
            swallowed
        );
    }



    private static bool ContainsHandling(
        string body)
    {
        return
            body.Contains(
                "throw",
                StringComparison.OrdinalIgnoreCase)

            ||

            body.Contains(
                "log",
                StringComparison.OrdinalIgnoreCase)

            ||

            body.Contains(
                "logger",
                StringComparison.OrdinalIgnoreCase)

            ||

            body.Contains(
                "Console.",
                StringComparison.OrdinalIgnoreCase)

            ||

            body.Contains(
                "print",
                StringComparison.OrdinalIgnoreCase);
    }



    private string? ResolveLayer(
        string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(
                layer =>
                    layer.Files.Contains(
                        file,
                        StringComparer.OrdinalIgnoreCase))
            ?.Name;
    }



    private static string[] GetExtensions(
        string language)
    {
        return language switch
        {
            "C#" =>
            [
                ".cs"
            ],

            "Java" =>
            [
                ".java"
            ],

            "Python" =>
            [
                ".py"
            ],

            _ =>
            []
        };
    }



    private static void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var fileResults =
            results
                .Where(x =>
                    x.Category == "ErrorHandling")
                .ToList();



        var summary =
            new ArchitectureEvaluatorResult(
                "ErrorHandlingEvaluator",
                projectPath)
            {
                Category =
                    "ErrorHandlingSummary"
            };



        summary.Metrics["FilesWithCatchBlocks"] =
            fileResults.Count;



        summary.Metrics["TotalCatchBlocks"] =
            fileResults.Sum(
                x =>
                    x.Metrics.GetValueOrDefault(
                        "TotalCatchBlocks"));



        results.Add(summary);
    }
}