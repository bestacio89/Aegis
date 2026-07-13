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
/// Evaluates domain model characteristics and produces metrics
/// that allow RuleEngine to identify anemic domain models.
///
/// The evaluator does not decide severity.
/// It only exposes architectural facts:
/// - State density
/// - Behavior density
/// - Entity signals
/// - Aggregate signals
///
/// Rule interpretation belongs to RuleEngine.
/// </summary>
public sealed class AnemicDomainModelEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;



    public override string Name =>
        "AnemicDomainModelEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java"
    ];



    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring Boot",
        "FastAPI",
        "Node",
        "Angular",
        "React",
        "Vue"
    ];




    private static readonly Regex ClassRegex =
        new(
            @"\bclass\s+([A-Z][A-Za-z0-9_]*)",
            RegexOptions.Compiled);



    private static readonly Regex PropertyRegex =
        new(
            @"\b(public|private|protected)\s+[A-Za-z0-9_<>,\[\]?]+\s+\w+\s*\{",
            RegexOptions.Compiled);



    private static readonly Regex MethodRegex =
        new(
            @"\b(public|private|protected|internal)\s+[A-Za-z0-9_<>,\[\]?]+\s+\w+\s*\(",
            RegexOptions.Compiled);



    private static readonly string[] DomainIndicators =
    [
        "Entity",
        "Aggregate",
        "Domain",
        "Model",
        "Root"
    ];



    public AnemicDomainModelEvaluator(
        ILogger<AnemicDomainModelEvaluator> logger,
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



        if (!_policy.DetectAnemicDomainModels)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Anemic domain model detection disabled by policy.");

            return results;
        }



        var files =
            EnumerateApplicationFiles(projectPath)
                .Where(IsSupportedSourceFile)
                .Where(IsDomainCandidateFile)
                .ToList();



        if (files.Count == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "No domain candidates detected.");

            return results;
        }



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



            foreach (Match match in ClassRegex.Matches(content))
            {
                token.ThrowIfCancellationRequested();



                var className =
                    match.Groups[1].Value;



                if (!IsDomainType(className))
                    continue;



                var classBlock =
                    ExtractClassBlock(
                        content,
                        match.Index);



                if (string.IsNullOrWhiteSpace(classBlock))
                    continue;



                var properties =
                    PropertyRegex
                        .Matches(classBlock)
                        .Count;



                var methods =
                    MethodRegex
                        .Matches(classBlock)
                        .Count;



                var behaviorRatio =
                    properties == 0
                        ? methods
                        :
                        (double)methods / properties;



                var domainBehaviorScore =
                    CalculateBehaviorScore(
                        properties,
                        methods);



                results.Add(
                    CreateResult(
                        file,
                        className,
                        properties,
                        methods,
                        behaviorRatio,
                        domainBehaviorScore));
            }
        }



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Anemic domain analysis completed with {results.Count} entries.");



        return results;
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        string className,
        int properties,
        int methods,
        double behaviorRatio,
        double behaviorScore)
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

            Category =
                "DesignPattern",


            Metrics =
            {
                ["Properties"] =
                    properties,

                ["Methods"] =
                    methods,

                ["BehaviorRatio"] =
                    behaviorRatio,

                ["DomainBehaviorScore"] =
                    behaviorScore
            },


            Metadata =
            {
                ["FilePath"] = file,

                ["ClassName"] =
                    className,

                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown",

                ["ArchitectureStyle"] =
                    string.Join(
                        ",",
                        Context?.Framework ?? "Unknown")
            }
        };
    }



    private static double CalculateBehaviorScore(
        int properties,
        int methods)
    {
        if (properties == 0)
            return methods > 0 ? 100 : 0;



        var ratio =
            (double)methods / properties;



        return Math.Round(
            Math.Min(
                ratio,
                1) *
            100,
            2);
    }



    private bool IsDomainCandidateFile(
        string file)
    {
        var normalized =
            file.Replace(
                '\\',
                '/');



        return normalized.Contains(
                   "/Domain/",
                   StringComparison.OrdinalIgnoreCase)

               ||

               normalized.Contains(
                   "/Core/",
                   StringComparison.OrdinalIgnoreCase)

               ||

               normalized.Contains(
                   "/Entities/",
                   StringComparison.OrdinalIgnoreCase)

               ||

               normalized.Contains(
                   "/Aggregates/",
                   StringComparison.OrdinalIgnoreCase);
    }



    private static bool IsSupportedSourceFile(
        string file)
    {
        return
            file.EndsWith(
                ".cs",
                StringComparison.OrdinalIgnoreCase)

            ||

            file.EndsWith(
                ".java",
                StringComparison.OrdinalIgnoreCase);
    }



    private static bool IsDomainType(
        string className)
    {
        return DomainIndicators.Any(
            indicator =>
                className.Contains(
                    indicator,
                    StringComparison.OrdinalIgnoreCase));
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



    private static string ExtractClassBlock(
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
}