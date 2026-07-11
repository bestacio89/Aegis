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



        var extensions =
            Context.Language switch
            {
                "C#" =>
                    new[] { ".cs" },

                "Java" =>
                    new[] { ".java" },

                "Python" =>
                    new[] { ".py" },

                _ =>
                    Array.Empty<string>()
            };



        if (extensions.Length == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                $"Unsupported language {Context.Language}.");

            return results;
        }



        var files =
            ResolveSourceFiles(
                projectPath,
                extensions);



        if (files.Count == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "No source files found for maintainability analysis.");

            return results;
        }



        double totalComplexity = 0;

        double totalCommentDensity = 0;



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();



            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



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



            totalComplexity += complexity;

            totalCommentDensity += commentDensity;



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
                        ResolveLayer(file),

                    DetectionConfidence =
                        Context.Confidence,


                    Category =
                        "Maintainability",


                    Metrics =
                    {
                        ["Complexity"] =
                            complexity,

                        ["LineCount"] =
                            lineCount,

                        ["CommentCount"] =
                            commentCount,

                        ["CommentDensity"] =
                            commentDensity
                    },


                    Metadata =
                    {
                        ["Language"] =
                            Context.Language,

                        ["Framework"] =
                            Context.Framework
                            ?? "Unknown"
                    }
                });
        }



        results.Add(
            CreateSummary(
                projectPath,
                files.Count,
                totalComplexity,
                totalCommentDensity));



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Maintainability evaluation completed with {results.Count} results.");



        return results;
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
                ["FileCount"] =
                    fileCount,

                ["AverageComplexity"] =
                    fileCount == 0
                        ? 0
                        :
                        totalComplexity /
                        fileCount,

                ["AverageCommentDensity"] =
                    fileCount == 0
                        ? 0
                        :
                        totalCommentDensity /
                        fileCount
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
}