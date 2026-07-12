using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Performance;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Performance;

/// <summary>
/// Evaluates performance-related architectural signals:
/// - algorithmic complexity risks
/// - blocking operations
/// - asynchronous maturity
/// - IO efficiency
/// - concurrency misuse
///
/// Produces:
/// - PerformanceHealthIndex
/// - EfficiencyHealthIndex
///
/// Framework agnostic.
/// Framework-specific optimizations belong to dedicated evaluators.
/// </summary>
public sealed class PerformanceEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly PerformancePolicy _policy;


    public override string Name =>
        "PerformanceEvaluator";


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
        "LanguageAgnostic"
    ];



    private static readonly Regex LoopRegex =
        new(
            @"\b(for|while|foreach)\b",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    private static readonly Regex NestedLoopRegex =
        new(
            @"\b(for|while|foreach)\b[\s\S]{0,300}\b(for|while|foreach)\b",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    private static readonly Regex BlockingRegex =
        new(
            @"\b(Thread\.Sleep|Task\.Wait|\.Result|time\.sleep|join\(\))\b",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    private static readonly Regex SyncIORegex =
        new(
            @"\b(File\.ReadAllText|ReadToEnd|readFileSync|open\(|Files\.readAll)\b",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    private static readonly Regex ThreadCreationRegex =
        new(
            @"\b(new\s+Thread|Task\.Run|ThreadPool|executor\.submit)\b",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



    private static readonly Regex StringConcatenationRegex =
        new(
            @"\+\s*=",
            RegexOptions.Compiled);



    private static readonly Regex LargeAllocationRegex =
        new(
            @"\b(new\s+(List|Array|Dictionary|HashMap|Set))\b",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);





    public PerformanceEvaluator(
        ILogger<PerformanceEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Performance ??
            new PerformancePolicy();
    }





    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();


        if (!_policy.Enabled)
        {
            _logger.LogInformation(
                "⏭ {Evaluator} disabled by policy.",
                Name);

            return results;
        }



        var files =
            Directory
                .EnumerateFiles(
                    projectPath,
                    "*.*",
                    SearchOption.AllDirectories)
                .Where(IsSupportedFile)
                .Where(f => !IsExcludedDir(f))
                .ToList();



        _logger.LogInformation(
            "🚀 Running {Evaluator} on {Count} files",
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
            catch
            {
                continue;
            }



            results.Add(
                EvaluateFile(
                    file,
                    content));
        }



        if (results.Count > 0)
        {
            AddSummary(
                results,
                projectPath);
        }



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} results",
            Name,
            results.Count);



        return results;
    }






    private ArchitectureEvaluatorResult EvaluateFile(
        string file,
        string content)
    {
        double algorithmic = 1;
        double concurrency = 1;
        double async = 1;
        double io = 1;



        var loopCount =
            LoopRegex.Matches(content).Count;



        if (loopCount > 0 &&
            NestedLoopRegex.IsMatch(content))
        {
            algorithmic -= 0.20;
        }



        if (_policy.DetectStringConcatenationInLoops &&
            StringConcatenationRegex.IsMatch(content) &&
            content.Contains("for",
                StringComparison.OrdinalIgnoreCase))
        {
            algorithmic -= 0.05;
        }



        if (_policy.DisallowBlockingCalls &&
            BlockingRegex.IsMatch(content))
        {
            concurrency -= 0.25;
        }



        if (_policy.WarnThreadCreationInHotPaths &&
            ThreadCreationRegex.IsMatch(content))
        {
            concurrency -= 0.15;
        }



        if (_policy.DetectLargeCollectionsInitialization &&
            LargeAllocationRegex.IsMatch(content))
        {
            algorithmic -= 0.05;
        }



        if (_policy.RequireAsyncForIOMethods &&
            SyncIORegex.IsMatch(content))
        {
            io -= 0.20;
        }



        if (_policy.SuggestAsyncStreams &&
            content.Contains("foreach") &&
            !content.Contains("await"))
        {
            async -= 0.10;
        }



        algorithmic =
            Normalize(algorithmic);

        concurrency =
            Normalize(concurrency);

        async =
            Normalize(async);

        io =
            Normalize(io);



        var health =
            ComputePerformanceHealth(
                algorithmic,
                concurrency,
                async,
                io);



        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            Category = "Performance",

            Metrics =
            new Dictionary<string, double>
            {
                ["AlgorithmicEfficiencyScore"] =
                    algorithmic * 100,

                ["ConcurrencyScore"] =
                    concurrency * 100,

                ["AsyncMaturityScore"] =
                    async * 100,

                ["IOMaturityScore"] =
                    io * 100,

                ["PerformanceHealthIndex"] =
                    health
            },


            Metadata =
            new Dictionary<string, string>
            {
                ["FileName"] =
                    Path.GetFileName(file),

                ["Language"] =
                    DetectLanguage(file),

                ["Evaluator"] =
                    Name
            }
        };
    }






    private static void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var health =
            results.Average(x =>
                x.Metrics.GetValueOrDefault(
                    "PerformanceHealthIndex",
                    0));



        results.Add(
            new ArchitectureEvaluatorResult(
                "PerformanceEvaluator",
                projectPath)
            {
                Category =
                    "PerformanceSummary",

                Metrics =
                new Dictionary<string, double>
                {
                    ["AnalyzedFiles"] =
                        results.Count,

                    ["AveragePerformanceHealthIndex"] =
                        health,

                    ["EfficiencyHealthIndex"] =
                        health
                },

                Metadata =
                new Dictionary<string, string>
                {
                    ["Evaluator"] =
                        "PerformanceEvaluator"
                }
            });
    }






    private static double ComputePerformanceHealth(
        double algorithmic,
        double concurrency,
        double async,
        double io)
    {
        var score =
            algorithmic * 0.35 +
            concurrency * 0.30 +
            async * 0.20 +
            io * 0.15;


        return Math.Round(
            score * 100,
            2);
    }





    private static double Normalize(double value)
        => Math.Max(
            0,
            Math.Min(
                1,
                value));





    private static bool IsSupportedFile(string file)
    {
        return
            file.EndsWith(".cs",
                StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".java",
                StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".py",
                StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".ts",
                StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".js",
                StringComparison.OrdinalIgnoreCase);
    }





    private static string DetectLanguage(string path)
    {
        if (path.EndsWith(".cs"))
            return "C#";

        if (path.EndsWith(".java"))
            return "Java";

        if (path.EndsWith(".py"))
            return "Python";

        if (path.EndsWith(".ts"))
            return "TypeScript";

        if (path.EndsWith(".js"))
            return "JavaScript";

        return "Unknown";
    }
}