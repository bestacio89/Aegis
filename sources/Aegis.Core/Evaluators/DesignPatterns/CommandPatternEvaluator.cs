using Aegis.Architecture.Diagnostics;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Aegis.Shared.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Evaluates adherence to the Command Pattern:
/// - Ensures Command/Handler pairing
/// - Detects overgrown (God) commands
/// - Flags handler invocations and forbidden dependencies
/// </summary>
public sealed class CommandPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;

    public override string Name => "CommandPatternEvaluator";

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


    private static readonly Regex CommandClassRx =
        new(@"class\s+(\w+Command)\b", RegexOptions.Compiled);

    private static readonly Regex HandlerClassRx =
        new(@"class\s+(\w+Handler)\b", RegexOptions.Compiled);

    private static readonly Regex MethodRx =
        new(@"\b(public|private|protected)\s+\w+\s*\(",
            RegexOptions.Compiled);


    public CommandPatternEvaluator(
        ILogger<CommandPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Architecture.DesignPatterns ?? new();
    }


    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.EnforceCommandPattern)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Info,
                "⚙️ Command pattern enforcement disabled by policy.");

            return results;
        }


        var files = EnumerateApplicationFiles(projectPath)
            .Where(file =>
                file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".java", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            .ToList();


        if (files.Count == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Info,
                "No relevant files found for Command Pattern evaluation.");

            return results;
        }


        var allHandlers = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);


        // Collect handler names first
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            var content =
                await File.ReadAllTextAsync(file, token);

            foreach (Match handler in HandlerClassRx.Matches(content))
            {
                allHandlers.Add(handler.Groups[1].Value);
            }
        }


        // Evaluate commands
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            var content =
                await File.ReadAllTextAsync(file, token);


            foreach (Match cmdMatch in CommandClassRx.Matches(content))
            {
                var commandName =
                    cmdMatch.Groups[1].Value;

                var expectedHandler =
                    commandName.Replace(
                        "Command",
                        "Handler",
                        StringComparison.OrdinalIgnoreCase);


                bool hasHandler =
                    allHandlers.Contains(expectedHandler);


                int methodCount =
                    MethodRx.Matches(content).Count;


                int forbiddenMatches =
                    _policy.ForbiddenInCommand.Count(
                        forbidden =>
                            content.Contains(
                                forbidden,
                                StringComparison.OrdinalIgnoreCase));


                int handlerInvocations =
                    _policy.HandlerInvocationHints.Count(
                        hint =>
                            Regex.IsMatch(
                                content,
                                $@"\b{hint}\s*\(",
                                RegexOptions.IgnoreCase));


                double handlerPairScore =
                    hasHandler ? 1.0 : 0.0;


                double complexityRatio =
                    Math.Min(
                        1.0,
                        methodCount /
                        (double)Math.Max(
                            1,
                            _policy.MaxMethodsPerCommand));


                double forbiddenRatio =
                    Math.Min(
                        1.0,
                        forbiddenMatches / 3.0);


                double invocationPenalty =
                    Math.Min(
                        1.0,
                        handlerInvocations / 2.0);


                double complianceScore =
                    ComputeCompliance(
                        handlerPairScore,
                        complexityRatio,
                        forbiddenRatio,
                        invocationPenalty);


                results.Add(new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "DesignPattern",

                    Metrics = new Dictionary<string, double>
                    {
                        ["HasHandlerPair"] = handlerPairScore,
                        ["MethodCount"] = methodCount,
                        ["ComplexityRatio"] = complexityRatio,
                        ["ForbiddenDependencyCount"] = forbiddenMatches,
                        ["HandlerInvocationCount"] = handlerInvocations,
                        ["CommandComplianceScore"] = complianceScore
                    },

                    Metadata = new Dictionary<string, string>
                    {
                        ["CommandName"] = commandName,
                        ["ExpectedHandler"] = expectedHandler,
                        ["Language"] =
                            Context?.Language ?? "Unknown",
                        ["Framework"] =
                            Context?.Framework ?? "Unknown",
                        ["Policy_RequireCommandHandlerPair"] =
                            _policy.RequireCommandHandlerPair.ToString(),
                        ["Policy_MaxMethodsPerCommand"] =
                            _policy.MaxMethodsPerCommand.ToString()
                    }
                });
            }
        }


        if (results.Count > 0)
        {
            double avgScore =
                results.Average(
                    result =>
                        result.Metrics.GetValueOrDefault(
                            "CommandComplianceScore",
                            0));


            double missingHandlers =
                results.Count(
                    result =>
                        result.Metrics.GetValueOrDefault(
                            "HasHandlerPair",
                            1) == 0);


            results.Add(new ArchitectureEvaluatorResult(Name, projectPath)
            {
                Category = "DesignPatternSummary",

                Metrics = new Dictionary<string, double>
                {
                    ["CommandCount"] = results.Count,
                    ["AverageCommandCompliance"] = avgScore,
                    ["MissingHandlerCount"] = missingHandlers,
                    ["OverallPatternHealth"] =
                        avgScore *
                        (1 - missingHandlers /
                         Math.Max(1, results.Count))
                },

                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] =
                        _policy.EnforceCommandPattern.ToString()
                }
            });
        }


        AegisDiagnostics.Report(
            Name,
            results.Count > 0
                ? DiagnosticLevel.Info
                : DiagnosticLevel.Warning,
            $"⚔️ Command pattern evaluation completed with {results.Count} metric entries.");


        return results;
    }


    private static double ComputeCompliance(
        double hasHandler,
        double complexity,
        double forbidden,
        double invocations)
    {
        double score =
            hasHandler * 0.4 +
            (1 - complexity) * 0.25 +
            (1 - forbidden) * 0.2 +
            (1 - invocations) * 0.15;

        return Math.Round(score * 100, 2);
    }
}