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
/// </summary>
public sealed class ArchitecturalPerformanceEvaluator
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
            @"\b(?:Thread\.Sleep|Task\.Wait|[A-Za-z_][A-Za-z0-9_]*\.Wait|time\.sleep|Thread\.Join)\s*\(",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);

    // .Result also names ordinary status/result objects. Do not count an
    // awaited result access as synchronous blocking.
    private static readonly Regex SynchronousResultRegex = new(
        @"(?m)^(?![^\r\n]*\bawait\b)[^\r\n]*\.Result\s*(?:;|,|\)|$)",
        RegexOptions.Compiled);

    private static readonly Regex NonCodeRegex = new(
        """"(?s)(?:\x22{3}.*?\x22{3}|\x27{3}.*?\x27{3}|\x60(?:\\.|[^\x60\\])*\x60|@?"(?:""|\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|//[^\r\n]*|/\*.*?\*/|\#[^\r\n]*)"""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);



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



    public ArchitecturalPerformanceEvaluator(
        ILogger<ArchitecturalPerformanceEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Performance ??
            new PerformancePolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
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



        var files = ResolveSourceFiles(
            projectPath,
            ".cs",
            ".java",
            ".py",
            ".ts",
            ".js");



        _logger.LogInformation(
            "🚀 Running {Evaluator} on {Count} files",
            Name,
            files.Count);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            try
            {
                var content =
                    await File.ReadAllTextAsync(
                        file,
                        token);


                results.Add(
                    EvaluateFile(
                        file,
                        content));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Unable to evaluate file {File}",
                    file);
            }
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
        var language = DetectLanguage(file);
        var sanitized = MaskNonCode(content);
        var score = AnalyzePerformanceSignals(sanitized);



        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            Category =
                "Performance",

            Metrics =
            {
                ["AlgorithmicEfficiencyScore"] =
                    score.Algorithmic * 100,

                ["ConcurrencyScore"] =
                    score.Concurrency * 100,

                ["AsyncMaturityScore"] =
                    score.Async * 100,

                ["IOMaturityScore"] =
                    score.IO * 100,

                ["BlockingIOCount"] =
                    BlockingRegex.Matches(sanitized).Count +
                    SynchronousResultRegex.Matches(sanitized).Count +
                    SyncIORegex.Matches(sanitized).Count,

                ["PerformanceHealthIndex"] =
                    score.Health
            },

            Metadata =
            {
                ["FileName"] =
                    Path.GetFileName(file),

                ["FilePath"] =
                    file,

                ["Language"] =
                    language,

                ["Evaluator"] =
                    Name,

                ["EvaluationScope"] =
                    BaseArchitectureEvaluator.FileScope
            }
        };
    }



    private static string MaskNonCode(string content)
    {
        return NonCodeRegex.Replace(content, match =>
            string.Create(
                match.Length,
                match.Value,
                static (span, value) =>
                {
                    for (var index = 0; index < value.Length; index++)
                    {
                        span[index] = value[index] is '\r' or '\n'
                            ? value[index]
                            : ' ';
                    }
                }));
    }

    private PerformanceScore AnalyzePerformanceSignals(
        string content)
    {
        var algorithmic = 1d;
        var concurrency = 1d;
        var async = 1d;
        var io = 1d;



        var hasLoop =
            LoopRegex.IsMatch(content);



        if (hasLoop &&
            NestedLoopRegex.IsMatch(content))
        {
            algorithmic -= 0.20;
        }



        if (_policy.DetectStringConcatenationInLoops &&
            hasLoop &&
            StringConcatenationRegex.IsMatch(content))
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
            hasLoop &&
            !content.Contains(
                "await",
                StringComparison.OrdinalIgnoreCase))
        {
            async -= 0.10;
        }



        algorithmic = Normalize(algorithmic);
        concurrency = Normalize(concurrency);
        async = Normalize(async);
        io = Normalize(io);



        return new PerformanceScore
        {
            Algorithmic = algorithmic,
            Concurrency = concurrency,
            Async = async,
            IO = io,

            Health =
                ComputePerformanceHealth(
                    algorithmic,
                    concurrency,
                    async,
                    io)
        };
    }



    private static void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var analyzedFiles =
            results.Count;



        var health =
            results.Average(
                x =>
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
                {
                    ["AnalyzedFiles"] =
                        analyzedFiles,

                    ["AveragePerformanceHealthIndex"] =
                        health,

                    ["EfficiencyHealthIndex"] =
                        health
                },

                Metadata =
                {
                    ["Evaluator"] =
                        "PerformanceEvaluator",

                    ["EvaluationScope"] =
                        BaseArchitectureEvaluator.ProjectScope
                }
            });
    }



    private static double ComputePerformanceHealth(
        double algorithmic,
        double concurrency,
        double async,
        double io)
    {
        return Math.Round(
            (
                algorithmic * 0.35 +
                concurrency * 0.30 +
                async * 0.20 +
                io * 0.15
            ) * 100,
            2);
    }



    private static double Normalize(
        double value)
        =>
            Math.Clamp(
                value,
                0,
                1);



    private static bool IsSupportedFile(
        string file)
    {
        return
            file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".java", StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".js", StringComparison.OrdinalIgnoreCase);
    }



    private static string DetectLanguage(
        string path)
    {
        if (path.EndsWith(".cs",
                StringComparison.OrdinalIgnoreCase))
            return "C#";


        if (path.EndsWith(".java",
                StringComparison.OrdinalIgnoreCase))
            return "Java";


        if (path.EndsWith(".py",
                StringComparison.OrdinalIgnoreCase))
            return "Python";


        if (path.EndsWith(".ts",
                StringComparison.OrdinalIgnoreCase))
            return "TypeScript";


        if (path.EndsWith(".js",
                StringComparison.OrdinalIgnoreCase))
            return "JavaScript";


        return "Unknown";
    }



    private sealed class PerformanceScore
    {
        public double Algorithmic { get; init; }

        public double Concurrency { get; init; }

        public double Async { get; init; }

        public double IO { get; init; }

        public double Health { get; init; }
    }
}