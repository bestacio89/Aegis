using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.Architecture;


/// <summary>
/// Evaluates documentation coverage of public architectural elements.
///
/// Documentation evaluation is based on discoverable public contracts,
/// not raw file comments or line counts.
///
/// The evaluator reports only documentation violations.
/// Successful documentation coverage is handled by aggregate metrics.
/// </summary>
public sealed class DocumentationEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private const double MinimumDocumentationCoverage = 80.0;



    public override string Name =>
        "DocumentationEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "TypeScript",
        "JavaScript",
        "Java",
        "Python"
    ];



    public override string[] SupportedFrameworks =>
    [
        "*"
    ];



    private static readonly Regex PublicDeclarationRegex =
        new(
            @"\b(public|export)\s+(class|interface|function|record)\s+\w+",
            RegexOptions.Compiled);



    public DocumentationEvaluator(
        ILogger<DocumentationEvaluator> logger)
        : base(logger)
    {
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
            return results;



        var totalDeclarations = 0;

        var documentedDeclarations = 0;



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            if (IsGeneratedOrTest(file))
                continue;



            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



            var declarations =
                PublicDeclarationRegex
                    .Matches(content);



            if (declarations.Count == 0)
                continue;



            var documented =
                CountDocumentedDeclarations(
                    content,
                    Context.Language);



            totalDeclarations +=
                declarations.Count;



            documentedDeclarations +=
                Math.Min(
                    documented,
                    declarations.Count);
        }



        if (totalDeclarations == 0)
            return results;



        var coverage =
            (double)documentedDeclarations /
            totalDeclarations *
            100;



        if (coverage < MinimumDocumentationCoverage)
        {
            results.Add(
                CreateViolation(
                    projectPath,
                    coverage,
                    totalDeclarations,
                    documentedDeclarations));
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Documentation evaluation completed. Coverage: {coverage:0.0}%");



        return results;
    }



    private ArchitectureEvaluatorResult CreateViolation(
        string projectPath,
        double coverage,
        int totalDeclarations,
        int documentedDeclarations)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            Category = "Documentation",


            Metrics =
            {
                ["DocumentationCoverage"] =
                    coverage,

                ["TotalDeclarations"] =
                    totalDeclarations,

                ["DocumentedDeclarations"] =
                    documentedDeclarations,

                ["MissingDocumentation"] =
                    totalDeclarations -
                    documentedDeclarations
            },


            Metadata =
            {
                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown",

                ["RequiredCoverage"] =
                    $"{MinimumDocumentationCoverage:0.0}%",

                ["ActualCoverage"] =
                    $"{coverage:0.0}%"
            }
        };
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

                "TypeScript" =>
                [
                    ".ts"
                ],

                "JavaScript" =>
                [
                    ".js"
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
                        extension =>
                            file.EndsWith(
                                extension,
                                StringComparison.OrdinalIgnoreCase)))
            .Where(
                file =>
                    !IsExcludedDirectory(file))
            .ToList();
    }



    private static int CountDocumentedDeclarations(
        string content,
        string language)
    {
        return language switch
        {
            "C#" =>
                Regex.Matches(
                    content,
                    @"///\s*<summary>")
                .Count,


            "Java" =>
                Regex.Matches(
                    content,
                    @"/\*\*")
                .Count,


            "Python" =>
                Regex.Matches(
                    content,
                    "\"\"\"")
                .Count / 2,


            "TypeScript" or "JavaScript" =>
                Regex.Matches(
                    content,
                    @"/\*\*")
                .Count,


            _ => 0
        };
    }



    private static bool IsGeneratedOrTest(
        string file)
    {
        return
            file.Contains(
                "Test",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.Contains(
                "Generated",
                StringComparison.OrdinalIgnoreCase);
    }



    private static bool IsExcludedDirectory(
        string file)
    {
        return
            file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase);
    }
}