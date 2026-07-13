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


public sealed class MaintainabilityEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly MaintainabilityPolicy _policy;



    public override string Name =>
        "MaintainabilityEvaluator";



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



    public MaintainabilityEvaluator(
        ILogger<MaintainabilityEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Maintainability
            ?? new MaintainabilityPolicy();
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
                Context);



        if (files.Count == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "No source files found for maintainability analysis.");

            return results;
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Analyzing maintainability for {files.Count} files " +
            $"({Context.Language}/{Context.Framework}).");



        double totalComplexity = 0;

        double totalCommentDensity = 0;



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            if (IsExcludedFile(file))
                continue;



            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



            var metrics =
                AnalyzeMaintainability(
                    content);



            totalComplexity +=
                metrics.Complexity;



            totalCommentDensity +=
                metrics.CommentDensity;



            results.Add(
                CreateResult(
                    file,
                    metrics));
        }



        results.Add(
            CreateSummary(
                projectPath,
                results.Count,
                totalComplexity,
                totalCommentDensity));



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Maintainability evaluation completed with {results.Count} results.");



        return results;
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        MaintainabilityMetrics metrics)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            ProjectName =
                Context?.ProjectName,

            Language =
                Context?.Language,

            Framework =
                Context?.Framework,

            Layer =
                ResolveLayer(file),

            DetectionConfidence =
                Context?.Confidence ?? 0,

            File =
                file,


            Category =
                "Maintainability",


            Metrics =
            {
                ["Complexity"] =
                    metrics.Complexity,

                ["LineCount"] =
                    metrics.LineCount,

                ["CommentCount"] =
                    metrics.CommentCount,

                ["CommentDensity"] =
                    metrics.CommentDensity
            },


            Metadata =
            {
                ["FilePath"] = file,

                ["Language"] =
                    Context?.Language
                    ?? "Unknown",

                ["Framework"] =
                    Context?.Framework
                    ?? "Unknown",

                ["Layer"] =
                    ResolveLayer(file)
                    ?? "Unknown"
            }
        };
    }



    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        int fileCount,
        double totalComplexity,
        double totalCommentDensity)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            ProjectName =
                Context?.ProjectName,

            Language =
                Context?.Language,

            Framework =
                Context?.Framework,

            DetectionConfidence =
                Context?.Confidence ?? 0,


            Category =
                "MaintainabilitySummary",


            Metrics =
            {
                ["AnalyzedFiles"] =
                    fileCount,

                ["AverageComplexity"] =
                    fileCount == 0
                        ? 0
                        :
                        Math.Round(
                            totalComplexity / fileCount,
                            2),

                ["AverageCommentDensity"] =
                    fileCount == 0
                        ? 0
                        :
                        Math.Round(
                            totalCommentDensity / fileCount,
                            2)
            },


            Metadata =
            {
                ["Language"] =
                    Context?.Language
                    ?? "Unknown",

                ["Framework"] =
                    Context?.Framework
                    ?? "Unknown"
            }
        };
    }



    private MaintainabilityMetrics AnalyzeMaintainability(
        string content)
    {
        var lineCount =
            content.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries)
            .Length;



        var commentCount =
            CountComments(content);



        var complexity =
            CountComplexity(content);



        var commentDensity =
            lineCount == 0
                ? 0
                :
                (double)commentCount /
                lineCount *
                100;



        return new MaintainabilityMetrics(
            complexity,
            lineCount,
            commentCount,
            commentDensity);
    }



    private static List<string> ResolveSourceFiles(
        string root,
        ProjectArchitectureContext context)
    {
        var extensions =
            context.Language switch
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
                    Array.Empty<string>()
            };



        return Directory
            .EnumerateFiles(
                root,
                "*.*",
                SearchOption.AllDirectories)
            .Where(
                file =>
                    extensions.Any(
                        ext =>
                            file.EndsWith(
                                ext,
                                StringComparison.OrdinalIgnoreCase)))
            .Where(
                file =>
                    !IsExcludedDir(file))
            .ToList();
    }



    private string ResolveLayer(
        string file)
    {
        if (Context is null)
            return "Unknown";



        return Context.Layers
            .FirstOrDefault(
                layer =>
                    layer.Files.Contains(
                        file,
                        StringComparer.OrdinalIgnoreCase))
            ?.Name
            ??
            "Unknown";
    }



    private static int CountComplexity(
        string content)
    {
        string[] keywords =
        [
            "if",
            "for",
            "while",
            "switch",
            "case",
            "catch",
            "&&",
            "||"
        ];



        return keywords.Sum(
            keyword =>
                Regex.Matches(
                    content,
                    Regex.Escape(keyword))
                .Count);
    }



    private static int CountComments(
        string content)
    {
        const string pattern =
            @"(//.*?$|/\*[\s\S]*?\*/|#.*?$)";



        return Regex.Matches(
                content,
                pattern,
                RegexOptions.Multiline)
            .Count;
    }



    private static bool IsExcludedFile(
        string file)
    {
        return
            file.Contains(
                "Generated",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.Contains(
                "Test",
                StringComparison.OrdinalIgnoreCase);
    }



    private static bool IsExcludedDir(
        string file)
    {
        return
            file.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.Contains(
                $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase);
    }



    private sealed record MaintainabilityMetrics(
        int Complexity,
        int LineCount,
        int CommentCount,
        double CommentDensity);
}