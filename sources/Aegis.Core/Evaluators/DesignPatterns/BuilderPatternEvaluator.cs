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
/// Evaluates Builder pattern characteristics.
///
/// Produces deterministic architectural facts:
/// - Builder detection
/// - Fluent API usage
/// - Build method existence
/// - Mutation signals
/// - Immutability signals
///
/// RuleEngine decides compliance and severity.
/// </summary>
public sealed class BuilderPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;



    public override string Name =>
        "BuilderPatternEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript",
        "Python"
    ];



    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring",
        "Angular",
        "FastAPI",
        "DDD",
        "Hexagonal",
        "Microservices",
        "EventDriven"
    ];



    private static readonly Regex BuilderClassRegex =
        new(
            @"\bclass\s+([A-Za-z0-9_]*Builder)\b",
            RegexOptions.Compiled);



    private static readonly Regex BuildMethodRegex =
        new(
            @"\bBuild\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex FluentMethodRegex =
        new(
            @"return\s+this\s*;",
            RegexOptions.Compiled);



    private static readonly Regex MutationRegex =
        new(
            @"\b(this\.)?\w+\s*=",
            RegexOptions.Compiled);



    public BuilderPatternEvaluator(
        ILogger<BuilderPatternEvaluator> logger,
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



        if (!_policy.EnforceBuilderPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Builder pattern evaluation disabled by policy.");

            return results;
        }



        var files =
            EnumerateApplicationFiles(projectPath)
                .Where(IsSupportedSourceFile)
                .ToList();



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



            foreach (Match match in BuilderClassRegex.Matches(content))
            {
                token.ThrowIfCancellationRequested();



                var builderName =
                    match.Groups[1].Value;



                var classBlock =
                    ExtractClassBlock(
                        content,
                        match.Index);



                if (string.IsNullOrWhiteSpace(classBlock))
                    continue;



                var hasBuild =
                    BuildMethodRegex.IsMatch(
                        classBlock);



                var fluentMethods =
                    FluentMethodRegex
                        .Matches(classBlock)
                        .Count;



                var mutations =
                    MutationRegex
                        .Matches(classBlock)
                        .Count;



                var immutableSignal =
                    HasImmutableSignal(
                        classBlock);



                results.Add(
                    CreateResult(
                        file,
                        builderName,
                        hasBuild,
                        fluentMethods,
                        mutations,
                        immutableSignal));
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
            $"Builder pattern evaluation completed with {results.Count} entries.");



        return results;
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        string builderName,
        bool hasBuild,
        int fluentMethods,
        int mutations,
        bool immutable)
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
                ["HasBuildMethod"] =
                    hasBuild ? 1 : 0,

                ["FluentMethodCount"] =
                    fluentMethods,

                ["MutationCount"] =
                    mutations,

                ["ImmutableSignal"] =
                    immutable ? 1 : 0
            },


            Metadata =
            {
                ["FilePath"] = file,
                ["BuilderClassName"] =
                    builderName,

                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown"
            }
        };
    }



    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        IEnumerable<ArchitectureEvaluatorResult> results)
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
                "DesignPatternSummary",


            Metrics =
            {
                ["BuilderCount"] =
                    results.Count(),

                ["AverageFluentMethods"] =
                    results.Average(
                        x =>
                            x.Metrics
                                .GetValueOrDefault(
                                    "FluentMethodCount")),

                ["AverageMutationCount"] =
                    results.Average(
                        x =>
                            x.Metrics
                                .GetValueOrDefault(
                                    "MutationCount")),

                ["AverageImmutableSignal"] =
                    results.Average(
                        x =>
                            x.Metrics
                                .GetValueOrDefault(
                                    "ImmutableSignal"))
            }
        };
    }



    private static bool HasImmutableSignal(
        string content)
    {
        return
            content.Contains(
                "readonly",
                StringComparison.OrdinalIgnoreCase)

            ||

            content.Contains(
                "Immutable",
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
                StringComparison.OrdinalIgnoreCase)

            ||

            file.EndsWith(
                ".ts",
                StringComparison.OrdinalIgnoreCase)

            ||

            file.EndsWith(
                ".py",
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