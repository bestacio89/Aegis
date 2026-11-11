using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Architecture.Evaluators.Analysis;
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
            @"\b(?:Thread\.Sleep|Task\.WaitAll|Task\.WaitAny|time\.sleep)\s*\(|\b[A-Za-z_][A-Za-z0-9_]*\.GetAwaiter\(\)\.GetResult\s*\(",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex TaskVariableDeclarationRegex = new(
        @"\b(?:System\.Threading\.Tasks\.)?(?:Task|ValueTask)(?:\s*<[^;=(){}]+>)?\??\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReaderVariableDeclarationRegex = new(
        @"\b(?:System\.IO\.)?(?:StreamReader|TextReader)\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);



    private static readonly Regex SyncIORegex =
        new(
            @"\b(?:(?:System\.IO\.)?File\.(?:ReadAllText|ReadAllBytes|ReadAllLines|WriteAllText|WriteAllBytes|WriteAllLines|OpenRead|OpenWrite|Open|CreateText)|readFileSync|Files\.readAll(?:Bytes|Lines|String)?|open)\s*\(",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);



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
        var sanitized = SourceCodeMasker.Mask(content);
        var score = AnalyzePerformanceSignals(sanitized);
        var blockingMatches = FindBlockingOperations(sanitized);
        var evidenceItems = blockingMatches
            .Select(match =>
            {
                var lineNumber = GetLineNumber(sanitized, match.Index);
                var sourceLine = GetSourceLine(content, lineNumber);
                return $"Line {lineNumber}: {match.Value.Trim()} — {sourceLine}";
            })
            .ToArray();

        var result = CreateResult(file, "Performance");
        result.ProjectName = Context?.ProjectName;
        result.Language = language;
        result.Framework = Context?.Framework;
        result.Layer = Context?.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .OrderByDescending(module => module.Path.Length)
            .FirstOrDefault(module =>
                IsSameOrDescendantPath(file, module.Path))
            ?.Type;
        result.DetectionConfidence = Context?.Confidence ?? 0;

        result.Metrics["AlgorithmicEfficiencyScore"] = score.Algorithmic * 100;
        result.Metrics["ConcurrencyScore"] = score.Concurrency * 100;
        result.Metrics["AsyncMaturityScore"] = score.Async * 100;
        result.Metrics["IOMaturityScore"] = score.IO * 100;
        result.Metrics["BlockingIOCount"] = blockingMatches.Count;
        result.Metrics["PerformanceHealthIndex"] = score.Health;

        result.Metadata["FileName"] = Path.GetFileName(file);
        result.Metadata["FilePath"] = file;
        result.Metadata["Language"] = language;
        result.Metadata["Framework"] = result.Framework ?? "Unknown";
        result.Metadata["Evaluator"] = Name;
        result.Metadata["EvaluationScope"] = BaseArchitectureEvaluator.FileScope;
        result.Metadata["AnalysisComplete"] = "true";
        result.Metadata["AnalysisStatus"] = "Completed";
        result.Metadata["MeasurementMethod"] = "SourcePatternHeuristic";

        if (evidenceItems.Length > 0)
        {
            var firstLine = GetLineNumber(sanitized, blockingMatches[0].Index);
            result.LineNumber = firstLine;
            result.Evidence = string.Join(Environment.NewLine, evidenceItems);
            result.Metadata["Evidence.BlockingIOCount"] =
                string.Join(Environment.NewLine, evidenceItems);
            result.Metadata["EvidenceLine.BlockingIOCount"] =
                firstLine.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result.Metadata["CodeSnippet.BlockingIOCount"] =
                GetSourceLine(content, firstLine);
        }

        return result;
    }

    private static List<Match> FindBlockingOperations(string content)
    {
        var matches = BlockingRegex.Matches(content)
            .Cast<Match>()
            .Concat(SyncIORegex.Matches(content).Cast<Match>())
            .ToList();

        var taskVariables = TaskVariableDeclarationRegex.Matches(content)
            .Select(match => match.Groups["name"].Value.TrimStart('@'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var variable in taskVariables)
        {
            var escaped = Regex.Escape(variable);
            var waitRegex = new Regex(
                $@"\b{escaped}\s*\.\s*Wait\s*\(",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            var resultRegex = new Regex(
                $@"\b{escaped}\s*\.\s*Result\b",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            matches.AddRange(waitRegex.Matches(content).Cast<Match>());
            matches.AddRange(resultRegex.Matches(content).Cast<Match>());
        }

        var readerVariables = ReaderVariableDeclarationRegex.Matches(content)
            .Select(match => match.Groups["name"].Value.TrimStart('@'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var variable in readerVariables)
        {
            var escaped = Regex.Escape(variable);
            var readToEndRegex = new Regex(
                $@"\b{escaped}\s*\.\s*ReadToEnd\s*\(",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            matches.AddRange(readToEndRegex.Matches(content).Cast<Match>());
        }

        return matches
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToList();
    }

    private static int GetLineNumber(string content, int position)
    {
        var lineNumber = 1;
        for (var index = 0; index < position && index < content.Length; index++)
        {
            if (content[index] == '\n')
                lineNumber++;
        }

        return lineNumber;
    }

    private static string GetSourceLine(string content, int lineNumber)
    {
        if (lineNumber <= 0)
            return string.Empty;

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        return lineNumber <= lines.Length
            ? lines[lineNumber - 1].Trim()
            : string.Empty;
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