using System.Text.RegularExpressions;

using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;


public sealed class CommandPatternEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DesignPatternPolicy _policy;



    public override string Name =>
        "CommandPatternEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "TypeScript",
        "JavaScript"
    ];



    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring",
        "NestJS",
        "FastAPI"
    ];



    private static readonly Regex CommandClassRegex =
        new(
            @"class\s+(\w+Command)\b",
            RegexOptions.Compiled);



    private static readonly Regex HandlerClassRegex =
        new(
            @"class\s+(\w+Handler)\b",
            RegexOptions.Compiled);



    private static readonly Regex MethodRegex =
        new(
            @"\b(public|private|protected)\s+\w+\s*\(",
            RegexOptions.Compiled);



    public CommandPatternEvaluator(
        ILogger<CommandPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Architecture.DesignPatterns
            ?? new();
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



        if (!_policy.EnforceCommandPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Command pattern evaluation disabled by policy.");

            return results;
        }



        var files =
            ResolveSourceFiles(
                projectPath,
                ResolveExtensions());



        if (files.Count == 0)
            return results;



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Evaluating command pattern across {files.Count} files.");



        var handlers =
            await DiscoverHandlersAsync(
                files,
                token);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



            foreach (Match commandMatch in CommandClassRegex.Matches(content))
            {
                var commandName =
                    commandMatch.Groups[1].Value;



                var expectedHandler =
                    commandName.Replace(
                        "Command",
                        "Handler",
                        StringComparison.OrdinalIgnoreCase);



                bool hasHandler =
                    handlers.Contains(
                        expectedHandler);



                var methodCount =
                    MethodRegex.Matches(content)
                        .Count;



                var forbiddenDependencies =
                    CountForbiddenDependencies(
                        content);



                var handlerInvocations =
                    CountHandlerInvocations(
                        content);



                var compliance =
                    ComputeCompliance(
                        hasHandler,
                        methodCount,
                        forbiddenDependencies,
                        handlerInvocations);



                results.Add(
                    CreateResult(
                        file,
                        commandName,
                        expectedHandler,
                        hasHandler,
                        methodCount,
                        forbiddenDependencies,
                        handlerInvocations,
                        compliance));
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
            $"Command pattern evaluation completed with {results.Count} entries.");



        return results;
    }



    private string[] ResolveExtensions()
    {
        return Context?.Language switch
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
            [
                ".cs",
                ".java",
                ".py",
                ".ts",
                ".js"
            ]
        };
    }



    private static async Task<HashSet<string>>
        DiscoverHandlersAsync(
            IEnumerable<string> files,
            CancellationToken token)
    {
        var handlers =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



            foreach (Match match in HandlerClassRegex.Matches(content))
            {
                handlers.Add(
                    match.Groups[1].Value);
            }
        }



        return handlers;
    }



    private int CountForbiddenDependencies(
        string content)
    {
        return _policy.ForbiddenInCommand
            .Count(
                forbidden =>
                    content.Contains(
                        forbidden,
                        StringComparison.OrdinalIgnoreCase));
    }



    private int CountHandlerInvocations(
        string content)
    {
        return _policy.HandlerInvocationHints
            .Count(
                hint =>
                    Regex.IsMatch(
                        content,
                        $@"\b{hint}\s*\(",
                        RegexOptions.IgnoreCase));
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        string commandName,
        string expectedHandler,
        bool hasHandler,
        int methods,
        int forbidden,
        int invocations,
        double compliance)
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
                nameof(
                    ArchitectureRuleCategory.DesignPatterns),


            Metrics =
            {
                ["HasHandlerPair"] =
                    hasHandler ? 1 : 0,

                ["MethodCount"] =
                    methods,

                ["ForbiddenDependencyCount"] =
                    forbidden,

                ["HandlerInvocationCount"] =
                    invocations,

                ["CommandComplianceScore"] =
                    compliance
            },


            Metadata =
            {
                ["CommandName"] =
                    commandName,

                ["ExpectedHandler"] =
                    expectedHandler,

                ["ArchitectureStyle"] =
                    Context?.ArchitectureStyle
                    ?? "Unknown",

                ["Layer"] =
                    ResolveLayer(file)
                    ?? "Unknown"
            }
        };
    }



    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        IEnumerable<ArchitectureEvaluatorResult> results)
    {
        var list =
            results.ToList();



        return new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            ProjectName =
                Context?.ProjectName,

            Category =
                "DesignPatternSummary",


            Metrics =
            {
                ["CommandCount"] =
                    list.Count,

                ["AverageCommandCompliance"] =
                    list.Average(
                        x =>
                            x.Metrics.GetValueOrDefault(
                                "CommandComplianceScore"))
            }
        };
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



    private double ComputeCompliance(
        bool hasHandler,
        int methods,
        int forbidden,
        int invocations)
    {
        var complexity =
            Math.Min(
                1,
                methods /
                (double)Math.Max(
                    1,
                    _policy.MaxMethodsPerCommand));



        var forbiddenRatio =
            Math.Min(
                1,
                forbidden / 3d);



        var invocationRatio =
            Math.Min(
                1,
                invocations / 2d);



        return Math.Round(
            (
                (hasHandler ? 1 : 0) * .4
                +
                (1 - complexity) * .25
                +
                (1 - forbiddenRatio) * .2
                +
                (1 - invocationRatio) * .15
            )
            * 100,
            2);
    }
}