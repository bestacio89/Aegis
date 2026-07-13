using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.DesignPatterns;

/// <summary>
/// Quantitatively evaluates Singleton pattern correctness across supported languages.
/// Computes a SingletonComplianceScore (0–100) based on thread safety,
/// initialization strategy, and pattern adherence consistency.
/// </summary>
public sealed class SingletonPatternEvaluator :
    BaseArchitectureEvaluator,
    IScopedDependency
{
    private readonly DesignPatternPolicy _policy;


    public override string Name =>
        "SingletonPatternEvaluator";


    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "TypeScript"
    ];


    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "Spring",
        "Flask",
        "FastAPI",
        "CleanArchitecture",
        "Hexagonal",
        "DDD",
        "Microservices"
    ];


    private static readonly Regex SingletonClassRx =
        new(
            @"class\s+(\w+)\b.*\bstatic\s+\1\s*",
            RegexOptions.Compiled |
            RegexOptions.Singleline);


    private static readonly Regex GetInstanceRx =
        new(
            @"getInstance\s*\(",
            RegexOptions.Compiled);


    private static readonly Regex ThreadLockRx =
        new(
            @"lock\s*\(|synchronized\s*\(",
            RegexOptions.Compiled);


    private static readonly Regex PythonSingletonRx =
        new(
            @"class\s+\w+\(Singleton\)",
            RegexOptions.Compiled);



    public SingletonPatternEvaluator(
        ILogger<SingletonPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();


        if (!_policy.EnforceSingletonPattern)
        {
            _logger.LogInformation(
                "⏭ {Evaluator} disabled by policy.",
                Name);

            return results;
        }


        var files =
            ResolveSourceFiles(
                projectPath,
                ".cs",
                ".java",
                ".py",
                ".ts");


        if (files.Count == 0)
        {
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
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to read singleton candidate file {File}",
                    file);

                continue;
            }



            var isSingleton =
                SingletonClassRx.IsMatch(content)
                ||
                GetInstanceRx.IsMatch(content)
                ||
                PythonSingletonRx.IsMatch(content);


            if (!isSingleton)
            {
                continue;
            }



            var classMatch =
                SingletonClassRx.Match(content);


            var className =
                classMatch.Success
                    ? classMatch.Groups[1].Value
                    : Path.GetFileNameWithoutExtension(file);



            var hasThreadSafety =
                ThreadLockRx.IsMatch(content);



            var isLazy =
                content.Contains(
                    "Lazy<",
                    StringComparison.OrdinalIgnoreCase)
                ||
                content.Contains(
                    "lazy",
                    StringComparison.OrdinalIgnoreCase)
                ||
                content.Contains(
                    "synchronized",
                    StringComparison.OrdinalIgnoreCase);



            var multipleInstances =
                SingletonClassRx.Matches(content).Count > 1;



            var namingValid =
                className.EndsWith(
                    _policy.SingletonSuffix,
                    StringComparison.OrdinalIgnoreCase);



            var threadSafetyScore =
                hasThreadSafety
                    ? 1d
                    : 0d;



            var lazyInitializationScore =
                isLazy
                    ? 1d
                    : 0d;



            var instanceDiscipline =
                multipleInstances
                    ? 0d
                    : 1d;



            var namingAdherence =
                namingValid
                    ? 1d
                    : 0d;



            var complianceScore =
                ComputeCompliance(
                    threadSafetyScore,
                    lazyInitializationScore,
                    instanceDiscipline,
                    namingAdherence);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    Category =
                        "DesignPattern",

                    Metrics =
                    {
                        ["IsSingleton"] = 1,

                        ["ThreadSafetyScore"] =
                            threadSafetyScore,

                        ["LazyInitializationScore"] =
                            lazyInitializationScore,

                        ["InstanceDisciplineScore"] =
                            instanceDiscipline,

                        ["NamingAdherenceScore"] =
                            namingAdherence,

                        ["SingletonComplianceScore"] =
                            complianceScore
                    },

                    Metadata =
                    {
                        ["FilePath"] =
                    file,
                        ["ClassName"] =
                            className,

                        ["Language"] =
                            Context?.Language
                            ?? "Unknown",

                        ["Framework"] =
                            Context?.Framework
                            ?? "Unknown",

                        ["Layer"] =
                            Context?.Layer
                            ?? "Unknown",

                        ["ThreadSafetyDetected"] =
                            hasThreadSafety.ToString(),

                        ["LazyInitializationDetected"] =
                            isLazy.ToString(),

                        ["MultipleInstancesFound"] =
                            multipleInstances.ToString(),

                        ["Policy_SingletonSuffix"] =
                            _policy.SingletonSuffix
                    }
                });
        }



        if (results.Count > 0)
        {
            var singletonResults =
                results.ToList();



            var averageScore =
                singletonResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "SingletonComplianceScore",
                            0));



            var averageThreadSafety =
                singletonResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "ThreadSafetyScore",
                            0));



            var averageLazy =
                singletonResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "LazyInitializationScore",
                            0));



            var violations =
                singletonResults.Count(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "InstanceDisciplineScore",
                            0) == 0);



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    projectPath)
                {
                    Category =
                        "DesignPatternSummary",

                    Metrics =
                    {
                        ["SingletonCount"] =
                            singletonResults.Count,

                        ["AverageComplianceScore"] =
                            averageScore,

                        ["AverageThreadSafety"] =
                            averageThreadSafety,

                        ["AverageLazyInitialization"] =
                            averageLazy,

                        ["ViolationCount"] =
                            violations,

                        ["OverallSingletonHealth"] =
                            averageScore *
                            (
                                1 -
                                violations /
                                (double)Math.Max(
                                    1,
                                    singletonResults.Count)
                            )
                    },

                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["PolicyEnabled"] =
                            _policy.EnforceSingletonPattern.ToString()
                    }
                });
        }



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} metric entries",
            Name,
            results.Count);


        return results;
    }



    private static double ComputeCompliance(
        double threadSafety,
        double lazyInit,
        double instanceDiscipline,
        double namingAdherence)
    {
        var score =
            threadSafety * 0.35 +
            lazyInit * 0.25 +
            instanceDiscipline * 0.30 +
            namingAdherence * 0.10;


        return Math.Round(
            score * 100,
            2);
    }
}