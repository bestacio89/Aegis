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
/// Quantitatively evaluates repository pattern adherence and data isolation discipline.
/// Detects and scores:
/// - Interface abstraction
/// - Async compliance
/// - Persistence leaks
/// - Excessive repository complexity
/// Produces RepositoryComplianceScore (0-100).
/// </summary>
public sealed class RepositoryPatternEvaluator :
    BaseArchitectureEvaluator,
    IScopedDependency
{
    private readonly DesignPatternPolicy _policy;

    public override string Name =>
        "RepositoryPatternEvaluator";


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
        "Django",
        "FastAPI",
        "Flask",
        "CleanArchitecture",
        "Hexagonal",
        "DDD",
        "Microservices"
    ];


    private static readonly Regex RepoClassRx =
        new(
            @"\bclass\s+(\w+Repository)\b",
            RegexOptions.Compiled);


    private static readonly Regex InterfaceRx =
        new(
            @"\binterface\s+I(\w+Repository)\b",
            RegexOptions.Compiled);


    private static readonly Regex MethodRx =
        new(
            @"\b(public|protected|private|def|function)\s+\w+\s*\(",
            RegexOptions.Compiled);


    private static readonly Regex FieldRx =
        new(
            @"\b(private|protected|var|self\.)\s*\w+",
            RegexOptions.Compiled);


    private static readonly Regex AsyncRx =
        new(
            @"async\s+|\bCompletableFuture<|\bTask<",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);


    private static readonly string[] LeakIndicators =
    [
        "SqlConnection",
        "DbContext",
        "EntityManager",
        "cursor.execute",
        "HttpClient",
        "axios",
        "fetch(",
        "open("
    ];


    public RepositoryPatternEvaluator(
        ILogger<RepositoryPatternEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Architecture.DesignPatterns
            ?? new DesignPatternPolicy();
    }


    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();


        if (!_policy.EnforceRepositoryPattern)
        {
            _logger.LogInformation(
                "🧩 {Evaluator} disabled by policy.",
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


        _logger.LogInformation(
            "📦 Running {Evaluator} on {Count} files.",
            Name,
            files.Count);


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
                    "Unable to read repository candidate file {File}",
                    file);

                continue;
            }


            foreach (Match match in RepoClassRx.Matches(content))
            {
                var repositoryName =
                    match.Groups[1].Value;


                var hasInterface =
                    InterfaceRx.IsMatch(content);


                var hasAsync =
                    AsyncRx.IsMatch(content);


                var hasPersistenceLeak =
                    LeakIndicators.Any(
                        indicator =>
                            content.Contains(
                                indicator,
                                StringComparison.OrdinalIgnoreCase));


                var namingCompliance =
                    repositoryName.EndsWith(
                        _policy.RepositorySuffix,
                        StringComparison.Ordinal);


                var methodCount =
                    MethodRx.Matches(content).Count;


                var fieldCount =
                    FieldRx.Matches(content).Count;


                var interfaceScore =
                    hasInterface ? 1d : 0d;


                var asyncScore =
                    hasAsync ? 1d : 0d;


                var leakScore =
                    hasPersistenceLeak ? 1d : 0d;


                var namingScore =
                    namingCompliance ? 1d : 0d;


                var complexity =
                    ComputeComplexity(
                        methodCount,
                        fieldCount);


                var compliance =
                    ComputeCompliance(
                        interfaceScore,
                        asyncScore,
                        leakScore,
                        namingScore,
                        complexity);


                results.Add(
                    new ArchitectureEvaluatorResult(
                        Name,
                        file)
                    {
                        Category =
                            "DesignPattern",

                        Metrics =
                        {
                            ["InterfaceAdherence"] =
                                interfaceScore,

                            ["AsyncCompliance"] =
                                asyncScore,

                            ["LeakRisk"] =
                                leakScore,

                            ["NamingCompliance"] =
                                namingScore,

                            ["RepositoryComplexity"] =
                                complexity,

                            ["RepositoryComplianceScore"] =
                                compliance
                        },

                        Metadata =
                        {
                            ["RepositoryName"] =
                                repositoryName,

                            ["Language"] =
                                Context?.Language
                                ?? "Unknown",

                            ["Framework"] =
                                Context?.Framework
                                ?? "Unknown",

                            ["Layer"] =
                                Context?.Layer
                                ?? "Unknown",

                            ["MethodCount"] =
                                methodCount.ToString(),

                            ["FieldCount"] =
                                fieldCount.ToString(),

                            ["HasInterface"] =
                                hasInterface.ToString(),

                            ["HasPersistenceLeak"] =
                                hasPersistenceLeak.ToString(),

                            ["HasAsyncMethods"] =
                                hasAsync.ToString(),

                            ["PolicyEnabled"] =
                                _policy.EnforceRepositoryPattern.ToString()
                        }
                    });
            }
        }


        if (results.Count > 0)
        {
            var repositoryResults =
                results.ToList();


            var averageCompliance =
                repositoryResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "RepositoryComplianceScore",
                            0));


            var averageComplexity =
                repositoryResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "RepositoryComplexity",
                            0));


            var leakCount =
                repositoryResults.Count(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "LeakRisk",
                            0) > 0);


            var asyncCompliance =
                repositoryResults.Average(
                    r =>
                        r.Metrics.GetValueOrDefault(
                            "AsyncCompliance",
                            0));


            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    projectPath)
                {
                    Category =
                        "DesignPatternSummary",

                    Metrics =
                    {
                        ["RepositoryCount"] =
                            repositoryResults.Count,

                        ["AverageComplianceScore"] =
                            averageCompliance,

                        ["AverageComplexity"] =
                            averageComplexity,

                        ["AverageAsyncCompliance"] =
                            asyncCompliance,

                        ["LeakCount"] =
                            leakCount,

                        ["OverallRepositoryHealth"] =
                            averageCompliance *
                            (
                                1 -
                                leakCount /
                                (double)Math.Max(
                                    1,
                                    repositoryResults.Count)
                            )
                    },

                    Metadata =
                    {
                        ["Evaluator"] =
                            Name,

                        ["PolicyEnabled"] =
                            _policy.EnforceRepositoryPattern.ToString()
                    }
                });
        }


        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} metric entries.",
            Name,
            results.Count);


        return results;
    }


    private static double ComputeComplexity(
        int methodCount,
        int fieldCount)
    {
        var normalized =
            Math.Min(
                1.0,
                methodCount / 30.0 +
                fieldCount / 50.0);


        return Math.Round(
            normalized,
            2);
    }


    private static double ComputeCompliance(
        double interfaceAdherence,
        double asyncCompliance,
        double leakRisk,
        double namingCompliance,
        double complexity)
    {
        var score =
            interfaceAdherence * 0.25 +
            asyncCompliance * 0.15 +
            (1 - leakRisk) * 0.35 +
            namingCompliance * 0.10 +
            (1 - complexity) * 0.15;


        return Math.Round(
            score * 100,
            2);
    }
}