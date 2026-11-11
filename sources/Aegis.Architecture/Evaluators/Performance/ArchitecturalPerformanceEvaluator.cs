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



    private static readonly Regex DotNetBlockingRegex =
        new(
            @"\b(?:Thread\.Sleep|Task\.WaitAll|Task\.WaitAny)\s*\(|\b[A-Za-z_][A-Za-z0-9_]*\.GetAwaiter\(\)\.GetResult\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JavaBlockingRegex =
        new(
            @"\bThread\.sleep\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonBlockingRegex =
        new(
            @"\btime\.sleep\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JavaScriptBlockingRegex =
        new(
            @"\bAtomics\.wait\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TaskVariableDeclarationRegex = new(
        @"\b(?:System\.Threading\.Tasks\.)?(?:Task|ValueTask)(?:\s*<[^;=(){}]+>)?\??\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReaderVariableDeclarationRegex = new(
        @"\b(?:System\.IO\.)?(?:StreamReader|TextReader)\s+(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);



    private static readonly Regex DotNetSyncIORegex =
        new(
            @"\b(?:(?:System\.IO\.)?File\.(?:ReadAllText|ReadAllBytes|ReadAllLines|WriteAllText|WriteAllBytes|WriteAllLines|OpenRead|OpenWrite|Open|CreateText))\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JavaSyncIORegex =
        new(
            @"\bFiles\.(?:readAllBytes|readAllLines|readString|write)\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonSyncIORegex =
        new(
            @"\bopen\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JavaScriptSyncIORegex =
        new(
            @"\b(?:readFileSync|writeFileSync)\s*\(",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);



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
            @"\bnew\s+(?:List|Dictionary|HashMap|HashSet)\s*(?:<[^>]+>)?\s*\(\s*(?<capacity>\d+)\s*\)|\bnew\s+[A-Za-z_][A-Za-z0-9_<>?]*\s*\[\s*(?<capacity>\d+)\s*\]",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static readonly Regex AwaitRegex = new(
        @"\bawait\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);



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
            ".tsx",
            ".js",
            ".jsx");



        _logger.LogInformation(
            "🚀 Running {Evaluator} on {Count} files",
            Name,
            files.Count);



        var unreadableFiles = new List<string>();

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;
            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException exception)
            {
                unreadableFiles.Add(file);
                _logger.LogWarning(exception, "Unable to read source file {FilePath}.", file);
                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                unreadableFiles.Add(file);
                _logger.LogWarning(exception, "Access denied while reading source file {FilePath}.", file);
                continue;
            }

            results.Add(EvaluateFile(file, content, projectPath));
        }

        if (results.Count > 0 || unreadableFiles.Count > 0)
            AddSummaries(results, projectPath, unreadableFiles);



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} results",
            Name,
            results.Count);


        return results;
    }



    private ArchitectureEvaluatorResult EvaluateFile(
        string file,
        string content,
        string projectPath)
    {
        var language = DetectLanguage(file);
        var sanitized = SourceCodeMasker.Mask(content);
        var loopAnalysis = AnalyzeLoopBodies(sanitized, language);
        var score = AnalyzePerformanceSignals(sanitized, language, loopAnalysis);
        var blockingCallMatches = FindBlockingCallOperations(sanitized, language);
        var syncIoMatches = _policy.RequireAsyncForIOMethods
            ? FindSynchronousIOOperations(sanitized, language)
            : Array.Empty<Match>();
        var blockingMatches = blockingCallMatches
            .Concat(syncIoMatches)
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToArray();
        var nestedLoopMatches = loopAnalysis.NestedLoopMatches;
        var threadCreationMatches = loopAnalysis.ThreadCreationMatches;
        var stringConcatenationMatches = loopAnalysis.StringConcatenationMatches;
        var largeAllocationMatches = loopAnalysis.LargeAllocationMatches;

        var framework = ResolveFrameworkForFile(file, projectPath, Context!);
        var result = CreateResult(file, "Performance");
        result.Language = language;
        result.Framework = framework;

        var owningModule = Context?.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .OrderByDescending(module => module.Path.Length)
            .FirstOrDefault(module => IsSameOrDescendantPath(file, module.Path));
        result.Layer = owningModule?.Layers.FirstOrDefault()?.Name
            ?? owningModule?.Type;
        result.DetectionConfidence = Context?.Confidence ?? 0;

        result.Metrics["AlgorithmicEfficiencyScore"] = score.Algorithmic * 100;
        result.Metrics["ConcurrencyScore"] = score.Concurrency * 100;
        result.Metrics["AsyncMaturityScore"] = score.Async * 100;
        result.Metrics["IOMaturityScore"] = score.IO * 100;
        result.Metrics["BlockingIOCount"] = blockingMatches.Count;
        result.Metrics["PerformanceHealthIndex"] = score.Health;
        result.Metrics["StringConcatenationAssignmentCount"] = stringConcatenationMatches.Length;
        result.Metrics["LargeAllocationSignalCount"] = largeAllocationMatches.Length;
        result.Metrics["LoopBodyLengthSignalCount"] = loopAnalysis.LongLoopBodyMatches.Count;
        result.Metrics["MaximumLoopNestingDepth"] = loopAnalysis.MaximumNestingDepth;

        result.Metadata["FileName"] = Path.GetFileName(file);
        result.Metadata["FilePath"] = file;
        result.Metadata["Language"] = language;
        result.Metadata["Framework"] = framework ?? "Unknown";
        result.Metadata["Evaluator"] = Name;
        result.Metadata["EvaluationScope"] = BaseArchitectureEvaluator.FileScope;
        result.Metadata["AnalysisComplete"] = bool.TrueString;
        result.Metadata["AnalysisStatus"] = "Completed";
        result.Metadata["MeasurementMethod"] = "SourcePatternHeuristic";
        result.Metadata["StringConcatenationInterpretation"] =
            "Informational only until concatenation can be associated with a specific loop body.";
        result.Metadata["LargeAllocationInterpretation"] =
            "Informational only; an allocation signal alone does not establish a performance defect.";
        result.Metadata["AsyncMaturityInterpretation"] =
            "Only configured blocking calls and synchronous I/O lower async maturity. A loop without I/O does not imply an async requirement.";

        var algorithmicMatches = nestedLoopMatches
            .Concat(loopAnalysis.LongLoopBodyMatches)
            .Concat(stringConcatenationMatches)
            .Concat(largeAllocationMatches)
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToArray();
        var healthMatches = algorithmicMatches
            .Concat(blockingMatches)
            .Concat(threadCreationMatches)
            .Concat(syncIoMatches)
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToArray();
        var asyncMatches = blockingCallMatches
            .Concat(syncIoMatches)
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToArray();
        var concurrencyMatches = blockingCallMatches
            .Concat(threadCreationMatches)
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToArray();

        var evidence = new List<string>();
        AddMetricEvidence(
            result,
            evidence,
            "PerformanceHealthIndex",
            healthMatches,
            content,
            sanitized,
            $"Performance health {score.Health:0.##}/100; algorithmic {score.Algorithmic * 100:0.##}, concurrency {score.Concurrency * 100:0.##}, async {score.Async * 100:0.##}, I/O {score.IO * 100:0.##}.");
        AddMetricEvidence(
            result,
            evidence,
            "AsyncMaturityScore",
            asyncMatches,
            content,
            sanitized,
            $"Async maturity {score.Async * 100:0.##}/100; configured blocking calls {blockingCallMatches.Count}; synchronous I/O operations {syncIoMatches.Length}.");
        AddMetricEvidence(
            result,
            evidence,
            "AlgorithmicEfficiencyScore",
            algorithmicMatches,
            content,
            sanitized,
            $"Algorithmic efficiency {score.Algorithmic * 100:0.##}/100; maximum loop nesting depth {loopAnalysis.MaximumNestingDepth} (policy maximum {_policy.MaxNestedLoopDepth}); " +
            $"loop bodies exceeding {_policy.MaxLoopBodyLength} lines: {loopAnalysis.LongLoopBodyMatches.Count}; " +
            $"string concatenation signals in loops: {stringConcatenationMatches.Length}; large allocations in loops: {largeAllocationMatches.Length}.");
        AddMetricEvidence(
            result,
            evidence,
            "ConcurrencyScore",
            concurrencyMatches,
            content,
            sanitized,
            $"Concurrency score {score.Concurrency * 100:0.##}/100; blocking calls {blockingCallMatches.Count}; thread-creation signals {threadCreationMatches.Length}.");
        AddMetricEvidence(
            result,
            evidence,
            "IOMaturityScore",
            syncIoMatches,
            content,
            sanitized,
            $"I/O maturity {score.IO * 100:0.##}/100; synchronous I/O operations detected {syncIoMatches.Length}.");
        AddMetricEvidence(
            result,
            evidence,
            "BlockingIOCount",
            blockingMatches,
            content,
            sanitized,
            $"Blocking I/O/blocking-operation count: {blockingMatches.Count}.");
        AddMetricEvidence(
            result,
            evidence,
            "StringConcatenationAssignmentCount",
            stringConcatenationMatches,
            content,
            sanitized,
            $"String concatenation assignment signals: {stringConcatenationMatches.Length} (informational only).");
        AddMetricEvidence(
            result,
            evidence,
            "LargeAllocationSignalCount",
            largeAllocationMatches,
            content,
            sanitized,
            $"Large collection allocation signals: {largeAllocationMatches.Length} (informational only).");
        AddMetricEvidence(
            result,
            evidence,
            "LoopBodyLengthSignalCount",
            loopAnalysis.LongLoopBodyMatches,
            content,
            sanitized,
            $"Loop bodies exceeding the configured line limit: {loopAnalysis.LongLoopBodyMatches.Count}.");
        AddMetricEvidence(
            result,
            evidence,
            "MaximumLoopNestingDepth",
            nestedLoopMatches,
            content,
            sanitized,
            $"Maximum measured loop nesting depth: {loopAnalysis.MaximumNestingDepth}.");

        result.Evidence = string.Join(Environment.NewLine, evidence);
        var firstEvidence = healthMatches.FirstOrDefault()
            ?? blockingMatches.FirstOrDefault()
            ?? threadCreationMatches.FirstOrDefault()
            ?? syncIoMatches.FirstOrDefault();
        if (firstEvidence is not null)
            result.LineNumber = GetLineNumber(sanitized, firstEvidence.Index);

        return result;
    }

    private static void AddMetricEvidence(
        ArchitectureEvaluatorResult result,
        ICollection<string> aggregateEvidence,
        string metricKey,
        IReadOnlyCollection<Match> matches,
        string source,
        string sanitized,
        string summary)
    {
        var evidence = matches.Count == 0
            ? summary
            : $"{summary}{Environment.NewLine}" + string.Join(
                Environment.NewLine,
                matches.Take(50).Select(match =>
                {
                    var line = GetLineNumber(sanitized, match.Index);
                    return $"Line {line}: {GetSourceLine(source, line)}";
                }));

        result.Metadata[$"Evidence.{metricKey}"] = evidence;
        result.Metadata[$"EvidenceSummary.{metricKey}"] = summary;
        result.Metadata[$"EvidenceScope.{metricKey}"] = "FileMetrics";
        aggregateEvidence.Add($"{metricKey}: {evidence}");

        var first = matches.FirstOrDefault();
        if (first is null)
            return;

        var lineNumber = GetLineNumber(sanitized, first.Index);
        result.Metadata[$"EvidenceLine.{metricKey}"] =
            lineNumber.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        result.Metadata[$"CodeSnippet.{metricKey}"] = GetSourceLine(source, lineNumber);
    }

    private static IReadOnlyCollection<Match> FindSynchronousIOOperations(
        string content,
        string language)
    {
        var regex = language switch
        {
            "C#" => DotNetSyncIORegex,
            "Java" => JavaSyncIORegex,
            "Python" => PythonSyncIORegex,
            "TypeScript" or "JavaScript" => JavaScriptSyncIORegex,
            _ => null
        };

        var matches = regex is null
            ? new List<Match>()
            : regex.Matches(content).Cast<Match>().ToList();

        if (language == "C#")
        {
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
        }

        return matches
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToArray();
    }

    private IReadOnlyCollection<Match> FindBlockingCallOperations(
        string content,
        string language)
    {
        if (!_policy.DisallowBlockingCalls)
            return Array.Empty<Match>();

        var matches = new List<Match>();
        var blockingRegex = GetBlockingRegex(language);
        if (blockingRegex is not null)
        {
            foreach (Match match in blockingRegex.Matches(content))
            {
                var isSleep = Regex.IsMatch(
                    match.Value,
                    @"(?:Thread\.Sleep|time\.sleep)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var isSynchronousWait = Regex.IsMatch(
                    match.Value,
                    @"(?:Task\.Wait|GetAwaiter|\.Wait|\.Result)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (isSleep && !_policy.CheckThreadSleepUsage)
                    continue;

                if (isSynchronousWait && !_policy.DetectSynchronousWaits)
                    continue;

                matches.Add(match);
            }
        }

        if (language == "C#" && _policy.DetectSynchronousWaits)
        {
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
        }

        return matches
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToArray();
    }

    private LoopAnalysis AnalyzeLoopBodies(
        string content,
        string language)
    {
        var loopMatches = LoopRegex.Matches(content).Cast<Match>().ToArray();
        var ranges = loopMatches
            .Select(match => TryGetLoopBodyRange(content, match, language, out var range)
                ? range
                : null)
            .Where(range => range is not null)
            .Cast<LoopBodyRange>()
            .ToArray();

        int GetDepth(LoopBodyRange parent)
        {
            var children = ranges
                .Where(candidate =>
                    candidate.LoopMatch.Index > parent.LoopMatch.Index
                    && candidate.Start >= parent.Start
                    && candidate.End <= parent.End)
                .ToArray();

            return children.Length == 0
                ? 1
                : 1 + children.Max(GetDepth);
        }

        var maximumDepth = ranges.Length == 0
            ? 0
            : ranges.Max(GetDepth);

        var nestedLoops = ranges
            .Where(parent => ranges.Any(candidate =>
                candidate.LoopMatch.Index > parent.LoopMatch.Index
                && candidate.Start >= parent.Start
                && candidate.End <= parent.End))
            .Select(range => range.LoopMatch)
            .ToArray();

        var longBodies = ranges
            .Where(range =>
                GetLineNumber(content, range.End)
                - GetLineNumber(content, range.Start)
                > _policy.MaxLoopBodyLength)
            .Select(range => range.LoopMatch)
            .ToArray();

        var stringConcatenations = _policy.DetectStringConcatenationInLoops
            ? StringConcatenationRegex.Matches(content)
                .Cast<Match>()
                .Where(match => ranges.Any(range =>
                    match.Index >= range.Start && match.Index < range.End))
                .ToArray()
            : Array.Empty<Match>();

        var largeAllocations = _policy.DetectLargeCollectionsInitialization
            ? FindLargeAllocationMatches(content)
                .Where(match => ranges.Any(range =>
                    match.Index >= range.Start && match.Index < range.End))
                .ToArray()
            : Array.Empty<Match>();

        var threadCreations = _policy.WarnThreadCreationInHotPaths
            ? ThreadCreationRegex.Matches(content)
                .Cast<Match>()
                .Where(match => ranges.Any(range =>
                    match.Index >= range.Start && match.Index < range.End))
                .ToArray()
            : Array.Empty<Match>();

        return new LoopAnalysis(
            ranges.Length,
            maximumDepth,
            nestedLoops,
            longBodies,
            stringConcatenations,
            largeAllocations,
            threadCreations);
    }

    private static bool TryGetLoopBodyRange(
        string content,
        Match loopMatch,
        string language,
        out LoopBodyRange range)
    {
        range = null!;

        if (language == "Python")
        {
            var colon = content.IndexOf(':', loopMatch.Index + loopMatch.Length);
            if (colon < 0)
                return false;

            var headerStart = content.LastIndexOf('\n', loopMatch.Index);
            headerStart = headerStart < 0 ? 0 : headerStart + 1;
            var headerIndent = CountLeadingWhitespace(content, headerStart, loopMatch.Index);
            var bodyStart = content.IndexOf('\n', colon);
            if (bodyStart < 0)
                return false;

            bodyStart++;
            var cursor = bodyStart;
            while (cursor < content.Length)
            {
                var lineEnd = content.IndexOf('\n', cursor);
                if (lineEnd < 0)
                    lineEnd = content.Length;

                var firstNonWhitespace = cursor;
                while (firstNonWhitespace < lineEnd
                    && char.IsWhiteSpace(content[firstNonWhitespace]))
                {
                    firstNonWhitespace++;
                }

                if (firstNonWhitespace < lineEnd)
                {
                    var indentation = CountLeadingWhitespace(content, cursor, firstNonWhitespace);
                    if (indentation <= headerIndent)
                        break;
                }

                cursor = lineEnd + 1;
            }

            range = new LoopBodyRange(loopMatch, bodyStart, cursor);
            return cursor > bodyStart;
        }

        var openingParenthesis = content.IndexOf(
            '(',
            loopMatch.Index + loopMatch.Length);
        if (openingParenthesis < 0)
            return false;

        var closingParenthesis = FindMatchingDelimiter(
            content,
            openingParenthesis,
            '(',
            ')');
        if (closingParenthesis < 0)
            return false;

        var bodyStartIndex = closingParenthesis + 1;
        while (bodyStartIndex < content.Length
            && char.IsWhiteSpace(content[bodyStartIndex]))
        {
            bodyStartIndex++;
        }

        if (bodyStartIndex >= content.Length)
            return false;

        if (content[bodyStartIndex] == '{')
        {
            var closingBrace = FindMatchingDelimiter(
                content,
                bodyStartIndex,
                '{',
                '}');
            if (closingBrace < 0)
                return false;

            range = new LoopBodyRange(
                loopMatch,
                bodyStartIndex + 1,
                closingBrace);
            return true;
        }

        var statementEnd = content.IndexOf(';', bodyStartIndex);
        if (statementEnd < 0)
            statementEnd = content.Length;

        range = new LoopBodyRange(loopMatch, bodyStartIndex, statementEnd);
        return statementEnd > bodyStartIndex;
    }

    private static int FindMatchingDelimiter(
        string content,
        int openingIndex,
        char opening,
        char closing)
    {
        var depth = 0;
        for (var index = openingIndex; index < content.Length; index++)
        {
            if (content[index] == opening)
                depth++;
            else if (content[index] == closing && --depth == 0)
                return index;
        }

        return -1;
    }

    private static int CountLeadingWhitespace(
        string content,
        int start,
        int end)
    {
        var count = 0;
        for (var index = start; index < end; index++)
        {
            if (content[index] == ' ')
                count++;
            else if (content[index] == '\t')
                count += 4;
            else
                break;
        }

        return count;
    }

    private IReadOnlyCollection<Match> FindLargeAllocationMatches(string content)
    {
        return LargeAllocationRegex.Matches(content)
            .Cast<Match>()
            .Where(match =>
                int.TryParse(match.Groups["capacity"].Value, out var capacity)
                && capacity >= _policy.LargeCollectionThreshold)
            .ToArray();
    }

    private IReadOnlyCollection<Match> FindBlockingOperations(
        string content,
        string language)
    {
        var matches = FindBlockingCallOperations(content, language).ToList();

        if (_policy.RequireAsyncForIOMethods)
            matches.AddRange(FindSynchronousIOOperations(content, language));

        return matches
            .GroupBy(match => match.Index)
            .Select(group => group.First())
            .OrderBy(match => match.Index)
            .ToArray();
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
        string content,
        string language,
        LoopAnalysis loopAnalysis)
    {
        var algorithmic = 1d;
        var concurrency = 1d;
        var async = 1d;
        var io = 1d;

        var blockingCalls = FindBlockingCallOperations(content, language);
        var synchronousIo = _policy.RequireAsyncForIOMethods
            ? FindSynchronousIOOperations(content, language)
            : Array.Empty<Match>();

        if (loopAnalysis.MaximumNestingDepth > _policy.MaxNestedLoopDepth)
            algorithmic -= 0.20;

        if (loopAnalysis.LongLoopBodyMatches.Count > 0)
            algorithmic -= 0.05;

        if (loopAnalysis.StringConcatenationMatches.Count > 0)
            algorithmic -= 0.05;

        if (loopAnalysis.LargeAllocationMatches.Count > 0)
            algorithmic -= 0.05;

        // A blocking call affects both responsiveness and concurrency health.
        if (blockingCalls.Count > 0)
        {
            concurrency -= 0.25;
            async -= 0.25;
        }

        if (loopAnalysis.ThreadCreationMatches.Count > 0)
            concurrency -= 0.15;

        if (synchronousIo.Count > 0)
        {
            io -= 0.20;
            async -= 0.20;
        }

        // A loop alone does not imply an async requirement. Only lower maturity
        // when synchronous I/O occurs in a loop without an awaitable path.
        if (_policy.SuggestAsyncStreams
            && loopAnalysis.LoopCount > 0
            && synchronousIo.Count > 0
            && !AwaitRegex.IsMatch(content))
        {
            async -= 0.05;
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



    private static Regex? GetBlockingRegex(string language) =>
        language switch
        {
            "C#" => DotNetBlockingRegex,
            "Java" => JavaBlockingRegex,
            "Python" => PythonBlockingRegex,
            "TypeScript" or "JavaScript" => JavaScriptBlockingRegex,
            _ => null
        };



    private void AddSummaries(
        List<ArchitectureEvaluatorResult> results,
        string projectPath,
        IReadOnlyCollection<string> unreadableFiles)
    {
        var fileResults = results.ToArray();
        var modules = Context?.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .OrderByDescending(module => module.Path.Length)
            .ToArray() ?? [];

        if (modules.Length == 0)
        {
            if (fileResults.Length > 0 || unreadableFiles.Count > 0)
            {
                results.Add(CreateProjectSummary(
                    Context?.ProjectName ?? Path.GetFileName(projectPath),
                    Path.GetFullPath(projectPath),
                    "Unknown",
                    Context?.Framework,
                    fileResults,
                    unreadableFiles));
            }

            return;
        }

        foreach (var module in modules)
        {
            var moduleResults = fileResults
                .Where(result =>
                {
                    var file = result.Metadata.GetValueOrDefault("FilePath");
                    var owner = modules.FirstOrDefault(candidate =>
                        !string.IsNullOrWhiteSpace(file)
                        && IsSameOrDescendantPath(file, candidate.Path));
                    return ReferenceEquals(owner, module);
                })
                .ToArray();

            var moduleUnreadable = unreadableFiles
                .Where(file =>
                {
                    var owner = modules.FirstOrDefault(candidate =>
                        IsSameOrDescendantPath(file, candidate.Path));
                    return ReferenceEquals(owner, module);
                })
                .ToArray();

            if (moduleResults.Length == 0 && moduleUnreadable.Length == 0)
                continue;

            results.Add(CreateProjectSummary(
                module.Name,
                module.Path,
                module.ProjectKind,
                module.Framework,
                moduleResults,
                moduleUnreadable));
        }

        var unownedResults = fileResults
            .Where(result =>
            {
                var file = result.Metadata.GetValueOrDefault("FilePath");
                return string.IsNullOrWhiteSpace(file)
                    || !modules.Any(module =>
                        IsSameOrDescendantPath(file, module.Path));
            })
            .ToArray();

        var unownedUnreadable = unreadableFiles
            .Where(file => !modules.Any(module =>
                IsSameOrDescendantPath(file, module.Path)))
            .ToArray();

        if (unownedResults.Length > 0 || unownedUnreadable.Length > 0)
        {
            results.Add(CreateProjectSummary(
                Context?.ProjectName ?? Path.GetFileName(projectPath),
                Path.GetFullPath(projectPath),
                "Unknown",
                Context?.Framework,
                unownedResults,
                unownedUnreadable));
        }
    }

    private ArchitectureEvaluatorResult CreateProjectSummary(
        string projectName,
        string projectPath,
        string projectKind,
        string? framework,
        IReadOnlyCollection<ArchitectureEvaluatorResult> fileResults,
        IReadOnlyCollection<string> unreadableFiles)
    {
        var health = fileResults.Count == 0
            ? 0
            : Math.Round(
                fileResults.Average(result =>
                    result.Metrics.GetValueOrDefault("PerformanceHealthIndex")),
                2);

        var evidence = fileResults
            .SelectMany(result =>
            {
                var filePath = result.Metadata.GetValueOrDefault("FilePath");
                var relativePath = string.IsNullOrWhiteSpace(filePath)
                    ? result.Target
                    : Path.GetRelativePath(projectPath, filePath);
                return (result.Evidence ?? string.Empty)
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => $"{relativePath}: {item}");
            })
            .Take(100)
            .ToArray();

        var complete = unreadableFiles.Count == 0;

        return new ArchitectureEvaluatorResult(Name, projectName)
        {
            ProjectName = projectName,
            Category = "PerformanceSummary",
            Evidence = evidence.Length == 0
                ? null
                : string.Join(Environment.NewLine, evidence),
            Metrics =
            {
                ["DiscoveredFiles"] = fileResults.Count + unreadableFiles.Count,
                ["AnalyzedFiles"] = fileResults.Count,
                ["UnreadableFiles"] = unreadableFiles.Count,
                ["DiagnosticCount"] = unreadableFiles.Count,
                ["AveragePerformanceHealthIndex"] = health,
                ["PerformanceHealthIndex"] = health,
                ["EfficiencyHealthIndex"] = health
            },
            Metadata =
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["ProjectName"] = projectName,
                ["ProjectPath"] = projectPath,
                ["ProjectKind"] = projectKind,
                ["Framework"] = framework ?? "Unknown",
                ["Evaluator"] = Name,
                ["AnalysisComplete"] = complete.ToString(),
                ["AnalysisStatus"] = complete
                    ? "Completed"
                    : "CompletedWithDiagnostics",
                ["AnalysisMethod"] = "SourcePatternHeuristic",
                ["Diagnostics"] = complete
                    ? string.Empty
                    : $"Unable to read {unreadableFiles.Count} source file(s) during performance analysis.",
                ["Evidence.PerformanceHealthIndex"] =
                    string.Join(Environment.NewLine, evidence),
                ["EvidenceSummary.PerformanceHealthIndex"] =
                    $"Analyzed {fileResults.Count} of {fileResults.Count + unreadableFiles.Count} discovered source files."
            }
        };
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
            file.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);
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
                StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            return "TypeScript";


        if (path.EndsWith(".js",
                StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase))
            return "JavaScript";


        return "Unknown";
    }



    private sealed record LoopBodyRange(
        Match LoopMatch,
        int Start,
        int End);

    private sealed record LoopAnalysis(
        int LoopCount,
        int MaximumNestingDepth,
        IReadOnlyCollection<Match> NestedLoopMatches,
        IReadOnlyCollection<Match> LongLoopBodyMatches,
        IReadOnlyCollection<Match> StringConcatenationMatches,
        IReadOnlyCollection<Match> LargeAllocationMatches,
        IReadOnlyCollection<Match> ThreadCreationMatches);

    private sealed class PerformanceScore
    {
        public double Algorithmic { get; init; }

        public double Concurrency { get; init; }

        public double Async { get; init; }

        public double IO { get; init; }

        public double Health { get; init; }
    }
}