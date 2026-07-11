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


/// <summary>
/// Evaluates method complexity and size against configured policy.
///
/// Produces violations only when methods exceed allowed complexity limits.
///
/// Discovery responsibility belongs to detectors.
/// Rule interpretation belongs to the RuleEngine.
/// </summary>
public sealed class ComplexityEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly ComplexityPolicy _policy;


    public override string Name =>
        "ComplexityEvaluator";


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



    private static readonly Regex MethodRegex =
        new(
            @"((public|private|protected|internal|def)\s+.*?(\{|\:))",
            RegexOptions.Compiled);



    private static readonly string[] ComplexityKeywords =
    [
        "if",
        "for",
        "foreach",
        "while",
        "switch",
        "case",
        "catch",
        "?",
        "&&",
        "||"
    ];



    public ComplexityEvaluator(
        ILogger<ComplexityEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Complexity
            ?? new ComplexityPolicy();
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
                "No source files found for complexity analysis.");

            return results;
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Analyzing complexity for {files.Count} files " +
            $"({Context.Language}/{Context.Framework}).");



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



            foreach (Match match in MethodRegex.Matches(content))
            {
                token.ThrowIfCancellationRequested();


                var method =
                    ExtractMethodBlock(
                        content,
                        match.Index);



                if (string.IsNullOrWhiteSpace(method))
                    continue;



                var complexity =
                    CountDecisionPoints(
                        method);



                var lines =
                    method
                        .Split(
                            '\n',
                            StringSplitOptions.RemoveEmptyEntries)
                        .Length;



                var violations =
                    EvaluateViolations(
                        complexity,
                        lines);



                if (violations.Count == 0)
                    continue;



                results.Add(
                    CreateViolation(
                        file,
                        complexity,
                        lines,
                        violations));
            }
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Complexity evaluation completed with {results.Count} violation(s).");



        return results;
    }



    private List<string> EvaluateViolations(
        int complexity,
        int lines)
    {
        var violations =
            new List<string>();


        if (complexity >
            _policy.MaxCyclomaticComplexity)
        {
            violations.Add(
                "CMP001");
        }



        if (lines >
            _policy.MaxLinesPerMethod)
        {
            violations.Add(
                "CMP002");
        }



        return violations;
    }



    private ArchitectureEvaluatorResult CreateViolation(
        string file,
        int complexity,
        int lines,
        IReadOnlyCollection<string> violations)
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
                "ComplexityViolation",


            Metrics =
            {
                ["CyclomaticComplexity"] =
                    complexity,

                ["MethodLineCount"] =
                    lines,

                ["Violation"] =
                    1
            },


            Metadata =
            {
                ["Rules"] =
                    string.Join(
                        ",",
                        violations),

                ["MaxCyclomaticThreshold"] =
                    _policy.MaxCyclomaticComplexity.ToString(),

                ["MaxLinesThreshold"] =
                    _policy.MaxLinesPerMethod.ToString(),

                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown"
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



    private static string ExtractMethodBlock(
        string content,
        int start)
    {
        var brace =
            content.IndexOf(
                '{',
                start);



        if (brace < 0)
            return string.Empty;



        var depth = 0;



        for (var i = brace; i < content.Length; i++)
        {
            if (content[i] == '{')
                depth++;



            if (content[i] == '}')
            {
                depth--;



                if (depth == 0)
                {
                    return content[
                        start..(i + 1)];
                }
            }
        }



        return string.Empty;
    }



    private static int CountDecisionPoints(
        string code)
    {
        return ComplexityKeywords.Sum(
            keyword =>
                Regex.Matches(
                    code,
                    Regex.Escape(keyword))
                .Count);
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
                "Generated",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.Contains(
                "Test",
                StringComparison.OrdinalIgnoreCase);
    }
}