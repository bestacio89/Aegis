using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

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



        var extensions =
            Context.Language switch
            {
                "C#" =>
                [".cs"],

                "Java" =>
                [".java"],

                "Python" =>
                [".py"],

                _ =>
                    Array.Empty<string>()
            };



        var files =
            ResolveSourceFiles(
                projectPath,
                extensions);



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



            var layer =
                ResolveLayer(file);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    ProjectName =
                        Context.ProjectName,

                    Language =
                        Context.Language,

                    Framework =
                        Context.Framework,

                    Layer =
                        layer,

                    DetectionConfidence =
                        Context.Confidence,


                    Category =
                        "ErrorHandling",


                    Metrics =
                    {
                        ["TotalCatchBlocks"] =
                            metrics.TotalCatchBlocks,

                        ["EmptyCatchBlocks"] =
                            metrics.EmptyCatchBlocks,

                        ["GenericCatchBlocks"] =
                            metrics.GenericCatchBlocks,

                        ["SwallowedExceptions"] =
                            metrics.SwallowedExceptions
                    },


                    Metadata =
                    {
                        ["Language"] =
                            Context.Language,

                        ["Framework"] =
                            Context.Framework
                            ?? "Unknown",

                        ["Layer"] =
                            layer
                            ?? "Unknown"
                    }
                });
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
        if (Context is null)
            return null;



        return Context.Layers
            .FirstOrDefault(
                layer =>
                    layer.Files.Contains(
                        file,
                        StringComparer.OrdinalIgnoreCase))
            ?.Name;
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



        results.Add(
            new ArchitectureEvaluatorResult(
                "ErrorHandlingEvaluator",
                projectPath)
            {
                Category =
                    "ErrorHandlingSummary",


                Metrics =
                {
                    ["FilesWithCatchBlocks"] =
                        fileResults.Count,

                    ["TotalCatchBlocks"] =
                        fileResults.Sum(
                            x =>
                                x.Metrics
                                    .GetValueOrDefault(
                                        "TotalCatchBlocks"))
                }
            });
    }
}