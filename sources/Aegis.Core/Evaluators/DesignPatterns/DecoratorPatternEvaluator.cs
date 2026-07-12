using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Evaluates proper implementation of the Decorator Pattern:
/// - Ensures interface delegation
/// - Validates constructor-based injection
/// - Checks delegation to wrapped components
/// Emits structured metrics for rule evaluation.
/// </summary>
public sealed class DecoratorPatternEvaluator : BaseArchitectureEvaluator
{
    private readonly DesignPatternPolicy _policy;


    public override string Name => "DecoratorPatternEvaluator";


    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "TypeScript"
    ];


    public override string[] SupportedFrameworks =>
    [
        "CleanArchitecture",
        "DDD",
        "Spring",
        "NestJS"
    ];



    private static readonly string[] SupportedExtensions =
    [
        ".cs",
        ".java",
        ".ts"
    ];



    private static readonly Regex InterfaceImplRx =
        new(
            @"class\s+\w+\s*:\s*\w+",
            RegexOptions.Compiled);



    private static readonly Regex InnerFieldRx =
        new(
            @"\b(private|protected)\s+\w+\s+_?\w*(Service|Component|Handler)\b",
            RegexOptions.Compiled);



    private static readonly Regex ConstructorInjectRx =
        new(
            @"\b(public|this)\s*\w*\(.*(Service|Component|Handler).*?\)",
            RegexOptions.Singleline | RegexOptions.Compiled);



    private static readonly Regex DelegateCallRx =
        new(
            @"\b_inner\.\w+\s*\(",
            RegexOptions.Compiled);



    public DecoratorPatternEvaluator(
        ILogger<DecoratorPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Architecture.DesignPatterns ?? new();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();


        if (!_policy.EnforceDecoratorPattern)
        {
            _logger.LogInformation(
                "🎭 Decorator pattern enforcement disabled by policy.");

            return results;
        }



        var files =
            EnumerateApplicationFiles(projectPath)
                .Where(IsSupportedFile)
                .ToList();



        if (files.Count == 0)
        {
            _logger.LogInformation(
                "🎭 No relevant files found for Decorator evaluation.");

            return results;
        }



        _logger.LogTrace(
            "🎭 Scanning {Count} files for Decorator pattern compliance.",
            files.Count);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            string content;

            try
            {
                content =
                    await File.ReadAllTextAsync(file, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to read file {File}",
                    file);

                continue;
            }



            bool implementsInterface =
                InterfaceImplRx.IsMatch(content);


            bool hasInnerComponent =
                InnerFieldRx.IsMatch(content);


            bool hasConstructorInjection =
                ConstructorInjectRx.IsMatch(content);


            bool hasDelegationCalls =
                DelegateCallRx.IsMatch(content);



            // Not a decorator candidate.
            if (!hasInnerComponent || !implementsInterface)
                continue;



            double injectionScore =
                hasConstructorInjection ? 1 : 0;


            double delegationScore =
                hasDelegationCalls ? 1 : 0;


            double complianceScore =
                Math.Round(
                    ((injectionScore + delegationScore) / 2) * 100,
                    2);



            results.Add(
                new ArchitectureEvaluatorResult(Name, file)
                {
                    Category = "DesignPattern",

                    Metrics =
                    {
                        ["HasInterfaceImplementation"] =
                            implementsInterface ? 1 : 0,

                        ["HasInnerComponent"] =
                            hasInnerComponent ? 1 : 0,

                        ["HasConstructorInjection"] =
                            injectionScore,

                        ["HasDelegationCalls"] =
                            delegationScore,

                        ["DecoratorComplianceScore"] =
                            complianceScore
                    },

                    Metadata =
                    {
                        ["FileName"] =
                            Path.GetFileName(file),

                        ["Language"] =
                            Context?.Language ?? "Unknown",

                        ["Framework"] =
                            Context?.Framework ?? "Unknown",

                        ["Policy_EnforceDecoratorPattern"] =
                            _policy.EnforceDecoratorPattern.ToString()
                    }
                });
        }



        if (results.Count > 0)
        {
            var avgScore =
                results.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "DecoratorComplianceScore",
                            0));


            var decoratorCount =
                results.Count;


            var missingInjection =
                results.Count(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "HasConstructorInjection",
                            1) == 0);


            var missingDelegation =
                results.Count(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "HasDelegationCalls",
                            1) == 0);



            results.Add(
                new ArchitectureEvaluatorResult(Name, projectPath)
                {
                    Category = "DesignPatternSummary",

                    Metrics =
                    {
                        ["DecoratorCount"] =
                            decoratorCount,

                        ["AverageComplianceScore"] =
                            avgScore,

                        ["MissingInjectionCount"] =
                            missingInjection,

                        ["MissingDelegationCount"] =
                            missingDelegation,

                        ["OverallDecoratorHealth"] =
                            avgScore *
                            (1 -
                             ((missingInjection + missingDelegation) /
                              Math.Max(1.0, decoratorCount)))
                    },

                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["PolicyEnabled"] =
                            _policy.EnforceDecoratorPattern.ToString()
                    }
                });
        }



        _logger.LogInformation(
            "🎭 {Evaluator} completed with {Count} metric entries",
            Name,
            results.Count);



        return results;
    }



    private static bool IsSupportedFile(string file)
    {
        return SupportedExtensions.Any(
            ext =>
                file.EndsWith(
                    ext,
                    StringComparison.OrdinalIgnoreCase));
    }
}