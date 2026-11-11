
using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators.Analysis;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Infrastructure;

public sealed class LoggingEvaluator : BaseArchitectureEvaluator
{
    private readonly LoggingPolicy _policy;

    private static readonly Regex LoggingCallRx = new(
        """
        \b(?:
            Log\s*\.\s*(?:Verbose|Trace|Debug|Information|Info|Warning|Warn|Error|Fatal|Critical)
            |
            logger\s*\.\s*(?:trace|debug|info|warning|warn|error|exception|critical|log)
            |
            logging\s*\.\s*(?:debug|info|warning|warn|error|exception|critical|log)
            |
            Console\s*\.\s*(?:WriteLine|Error\.WriteLine)
            |
            System\.Diagnostics\.Trace\s*\.\s*(?:WriteLine|TraceError|TraceWarning|TraceInformation)
            |
            \.Log(?:Trace|Debug|Information|Warning|Error|Critical)
        )\s*\(
        """,
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.IgnorePatternWhitespace,
        TimeSpan.FromMilliseconds(500));

    private static readonly Regex StructuredTemplateRx = new(
        @"\{[A-Za-z_][A-Za-z0-9_]*(?:,[^{}]*)?(?::[^{}]*)?\}",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex InterpolatedStringRx = new(
        """
        (?:
            \$"(?:[^"\\]|\\.)*"
            |
            \$@"(?:[^"]|"")*"
            |
            @\$"(?:[^"]|"")*"
            |
            f"(?:[^"\\]|\\.)*"
            |
            f'(?:[^'\\]|\\.)*'
        )
        """,
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.IgnorePatternWhitespace,
        TimeSpan.FromMilliseconds(500));

    private static readonly Regex PythonParameterizedLoggingRx = new(
        @"\b(?:logger|logging)\s*\.\s*(?:debug|info|warning|warn|error|exception|critical)\s*\(\s*['""][^'""]*%[sdif]",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex LogLevelRx = new(
        """
        \b(?:
            Log\s*\.\s*(?:Verbose|Trace|Debug|Information|Info|Warning|Warn|Error|Fatal|Critical)
            |
            logger\s*\.\s*(?:trace|debug|info|warning|warn|error|exception|critical)
            |
            logging\s*\.\s*(?:debug|info|warning|warn|error|exception|critical)
            |
            \.Log(?:Trace|Debug|Information|Warning|Error|Critical)
        )\s*\(
        """,
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.IgnorePatternWhitespace,
        TimeSpan.FromMilliseconds(500));

    public override string Name => "LoggingEvaluator";

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
        "*"
    ];

    public LoggingEvaluator(
        ILogger<LoggingEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Logging ?? new LoggingPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        token.ThrowIfCancellationRequested();

        var results = new List<ArchitectureEvaluatorResult>();

        if (!_policy.Enabled)
        {
            _logger.LogInformation("{Evaluator} is disabled by policy.", Name);
            return results;
        }

        if (!Directory.Exists(projectPath))
        {
            _logger.LogWarning(
                "{Evaluator} skipped: project directory does not exist: {ProjectPath}",
                Name,
                projectPath);
            return results;
        }

        var sourceFiles = ResolveSourceFiles(
                projectPath,
                ".cs", ".java", ".py", ".ts", ".tsx", ".js", ".jsx")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scopes = new Dictionary<string, ProjectLoggingScope>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in sourceFiles)
        {
            token.ThrowIfCancellationRequested();

            var projectScope = ResolveProjectScope(file, projectPath);
            if (!scopes.TryGetValue(projectScope.Path, out var scope))
            {
                scope = new ProjectLoggingScope(projectScope);
                scopes.Add(projectScope.Path, scope);
            }

            scope.AnalyzedFileCount++;

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
                scope.FailedFileCount++;
                _logger.LogDebug(exception, "Unable to read source file {FilePath}.", file);
                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                scope.FailedFileCount++;
                _logger.LogDebug(exception, "Access denied while reading source file {FilePath}.", file);
                continue;
            }

            var language = DetectLanguage(file);
            var code = SourceCodeMasker.Mask(content);
            var loggingCalls = LoggingCallRx.Matches(code);
            if (loggingCalls.Count == 0)
                continue;

            scope.LoggingFileCount++;

            var structuredScore = EvaluateStructuredLogging(content, code, language);
            var levelScore = EvaluateLogLevels(code);
            var noiseScore = EvaluateLoggingNoise(content, code, language);
            var integrityScore = ComputeIntegrity(
                structuredScore,
                levelScore,
                noiseScore);

            scope.FileResults.Add(CreateFileResult(
                file,
                language,
                loggingCalls,
                content,
                structuredScore,
                levelScore,
                noiseScore,
                integrityScore));
        }

        foreach (var scope in scopes.Values)
        {
            results.AddRange(scope.FileResults);

            results.Add(scope.FileResults.Count > 0
                ? CreateSummaryResult(scope)
                : CreateEmptySummaryResult(scope));
        }

        _logger.LogInformation(
            "{Evaluator} completed. Projects: {ProjectCount}; source files: {SourceFiles}; result entries: {ResultCount}.",
            Name,
            scopes.Count,
            sourceFiles.Count,
            results.Count);

        return results;
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string file,
        string language,
        MatchCollection loggingCalls,
        string content,
        double structured,
        double levels,
        double noise,
        double integrity)
    {
        var evidenceItems = loggingCalls
            .Cast<Match>()
            .Select(call =>
            {
                var lineNumber = GetLineNumber(content, call.Index);
                return $"{Path.GetFileName(file)}:{lineNumber}: {call.Value.Trim()} — {GetSourceLine(content, lineNumber)}";
            })
            .ToArray();

        var result = CreateResult(file, "Infrastructure");
        result.ProjectName ??= Context?.ProjectName;
        result.Language = language;
        result.Framework = Context?.Framework;
        result.Metrics["LoggingCallCount"] = loggingCalls.Count;
        result.Metrics["StructuredLoggingScore"] = structured;
        result.Metrics["LogLevelComplianceScore"] = levels;
        result.Metrics["LoggingNoiseScore"] = noise;
        result.Metrics["LoggingIntegrityScore"] = integrity;

        result.Metadata["FileName"] = Path.GetFileName(file);
        result.Metadata["FilePath"] = file;
        result.Metadata["Target"] = file;
        result.Metadata["Language"] = language;
        result.Metadata["Framework"] = result.Framework ?? "Unknown";
        result.Metadata["PolicyEnabled"] = _policy.Enabled.ToString();
        result.Metadata["EvaluationScope"] = FileScope;
        result.Metadata["AnalysisComplete"] = "true";
        result.Metadata["AnalysisStatus"] = "Completed";

        if (evidenceItems.Length > 0)
        {
            var firstLine = GetLineNumber(content, loggingCalls[0].Index);
            result.LineNumber = firstLine;
            result.Evidence = string.Join(Environment.NewLine, evidenceItems);
            result.Metadata["Evidence.LoggingIntegrityScore"] =
                string.Join(Environment.NewLine, evidenceItems);
            result.Metadata["EvidenceLine.LoggingIntegrityScore"] =
                firstLine.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result.Metadata["CodeSnippet.LoggingIntegrityScore"] =
                GetSourceLine(content, firstLine);
        }

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummaryResult(
        ProjectLoggingScope scope)
    {
        var integrity = Average(scope.FileResults, "LoggingIntegrityScore");
        var structured = Average(scope.FileResults, "StructuredLoggingScore");
        var levels = Average(scope.FileResults, "LogLevelComplianceScore");
        var noise = Average(scope.FileResults, "LoggingNoiseScore");
        var evidence = scope.FileResults
            .SelectMany(result => (result.Evidence ?? string.Empty)
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            .Take(100)
            .ToArray();

        var summary = new ArchitectureEvaluatorResult(Name, scope.Identity.Name)
        {
            ProjectName = scope.Identity.Name,
            Category = "InfrastructureSummary",
            Evidence = string.Join(Environment.NewLine, evidence),
            Metrics =
            {
                ["SourceFilesScanned"] = scope.AnalyzedFileCount,
                ["FilesWithLoggingCalls"] = scope.LoggingFileCount,
                ["FilesWithLoggingResults"] = scope.FileResults.Count,
                ["UnreadableFiles"] = scope.FailedFileCount,
                ["DiagnosticCount"] = scope.FailedFileCount,
                ["AverageIntegrityScore"] = integrity,
                ["LoggingIntegrityScore"] = integrity,
                ["AverageStructuredLogging"] = structured,
                ["AverageLogLevelCompliance"] = levels,
                ["AverageLoggingNoiseScore"] = noise,
                ["ObservabilityHealthIndex"] = integrity
            },
            Metadata =
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["ProjectName"] = scope.Identity.Name,
                ["ProjectPath"] = scope.Identity.Path,
                ["ProjectKind"] = scope.Identity.ProjectKind,
                ["Framework"] = scope.Identity.Framework ?? "Unknown",
                ["Evaluator"] = Name,
                ["PolicyEnabled"] = _policy.Enabled.ToString(),
                ["SupportedFrameworks"] = string.Join(", ", SupportedFrameworks),
                ["AnalysisComplete"] = (scope.FailedFileCount == 0).ToString(),
                ["AnalysisStatus"] = scope.FailedFileCount == 0
                    ? "Completed"
                    : "CompletedWithDiagnostics",
                ["Diagnostics"] = scope.FailedFileCount == 0
                    ? string.Empty
                    : $"Unable to read {scope.FailedFileCount} source file(s) during logging analysis.",
                ["ScoringNote"] =
                    "Scores are heuristic indicators based on recognized logging calls.",
                ["Evidence.LoggingIntegrityScore"] =
                    string.Join(Environment.NewLine, evidence),
                ["EvidenceSummary.LoggingIntegrityScore"] =
                    $"{scope.LoggingFileCount} of {scope.AnalyzedFileCount} analyzed source files contain recognized logging calls."
            }
        };

        return summary;
    }

    private ArchitectureEvaluatorResult CreateEmptySummaryResult(
        ProjectLoggingScope scope)
    {
        return new ArchitectureEvaluatorResult(Name, scope.Identity.Name)
        {
            ProjectName = scope.Identity.Name,
            Category = "InfrastructureSummary",
            Metrics =
            {
                ["SourceFilesScanned"] = scope.AnalyzedFileCount,
                ["FilesWithLoggingCalls"] = scope.LoggingFileCount,
                ["FilesWithLoggingResults"] = 0,
                ["UnreadableFiles"] = scope.FailedFileCount,
                ["DiagnosticCount"] = scope.FailedFileCount
            },
            Metadata =
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["ProjectName"] = scope.Identity.Name,
                ["ProjectPath"] = scope.Identity.Path,
                ["ProjectKind"] = scope.Identity.ProjectKind,
                ["Framework"] = scope.Identity.Framework ?? "Unknown",
                ["Evaluator"] = Name,
                ["PolicyEnabled"] = _policy.Enabled.ToString(),
                ["EvaluationStatus"] = scope.AnalyzedFileCount == 0
                    ? "NoSourceFilesFound"
                    : scope.LoggingFileCount == 0
                        ? "NoRecognizedLoggingCalls"
                        : "NoFilesEvaluated",
                ["AnalysisComplete"] = (scope.FailedFileCount == 0).ToString(),
                ["AnalysisStatus"] = scope.FailedFileCount == 0
                    ? "Completed"
                    : "CompletedWithDiagnostics",
                ["Diagnostics"] = scope.FailedFileCount == 0
                    ? string.Empty
                    : $"Unable to read {scope.FailedFileCount} source file(s) during logging analysis."
            }
        };
    }

    private ProjectIdentity ResolveProjectScope(string file, string projectPath)
    {
        var module = Context?.Modules
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Path))
            .OrderByDescending(candidate => candidate.Path.Length)
            .FirstOrDefault(candidate => IsSameOrDescendantPath(file, candidate.Path));

        return module is null
            ? new ProjectIdentity(
                Context?.ProjectName ?? Path.GetFileName(projectPath),
                Path.GetFullPath(projectPath),
                "Unknown",
                Context?.Framework)
            : new ProjectIdentity(
                module.Name,
                module.Path,
                module.ProjectKind,
                module.Framework);
    }

    private static bool IsSameOrDescendantPath(string candidate, string parent)
    {
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(
                normalizedCandidate,
                normalizedParent,
                StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
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
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        return lineNumber > 0 && lineNumber <= lines.Length
            ? lines[lineNumber - 1].Trim()
            : string.Empty;
    }

    private double EvaluateStructuredLogging(
        string content,
        string code,
        string language)
    {
        if (!_policy.RequireStructuredLogging)
        {
            return 100;
        }

        var calls = LoggingCallRx.Matches(code);

        if (calls.Count == 0)
        {
            return 100;
        }

        var structuredCalls = 0;
        var parameterizedCalls = 0;

        foreach (Match call in calls)
        {
            // Console and Trace output are observable diagnostics, but they
            // are not structured application-logger message templates.
            if (IsConsoleOrTraceCall(call.Value))
                continue;

            var invocation = ExtractInvocationText(content, code, call.Index);
            var hasTemplate = StructuredTemplateRx.IsMatch(invocation);
            var hasPythonParameters =
                language == "Python"
                && PythonParameterizedLoggingRx.IsMatch(invocation);
            if (hasTemplate)
                structuredCalls++;
            else if (hasPythonParameters)
                parameterizedCalls++;
        }

        // Structured message templates and parameterized Python logging
        // are positive evidence. Interpolation is not counted as structured.
        var positiveCalls = structuredCalls + parameterizedCalls;

        return Math.Round(
            100.0 * positiveCalls / calls.Count,
            2);
    }

    private double EvaluateLogLevels(string code)
    {
        if (!_policy.RequireLogLevels)
        {
            return 100;
        }

        var totalLoggingCalls = LoggingCallRx.Matches(code).Count;
        if (totalLoggingCalls == 0)
            return 100;

        var recognizedLevelCalls = LogLevelRx.Matches(content).Count;

        return Math.Round(
            100.0 * recognizedLevelCalls / totalLoggingCalls,
            2);
    }

    private double EvaluateLoggingNoise(
        string content,
        string code,
        string language)
    {
        if (!_policy.WarnInterpolatedStrings)
        {
            return 100;
        }

        var calls = LoggingCallRx.Matches(content);

        if (calls.Count == 0)
        {
            return 100;
        }

        var interpolatedCalls = 0;

        foreach (Match call in calls)
        {
            var invocation = ExtractInvocationText(content, call.Index);
            var interpolated = InterpolatedStringRx.IsMatch(invocation);
            var pythonPercentFormatting =
                language == "Python"
                && PythonParameterizedLoggingRx.IsMatch(invocation);

            if (interpolated && !pythonPercentFormatting)
                interpolatedCalls++;
        }

        var ratio = (double)interpolatedCalls / calls.Count;

        return Math.Round(
            Math.Clamp(100.0 * (1.0 - ratio), 0, 100),
            2);
    }

    private static bool IsConsoleOrTraceCall(string call) =>
        call.Contains("Console.", StringComparison.OrdinalIgnoreCase)
        || call.Contains("System.Diagnostics.Trace", StringComparison.OrdinalIgnoreCase);

    private static string ExtractInvocationText(
        string content,
        string code,
        int callStart)
    {
        var openingParenthesis = code.IndexOf('(', callStart);
        if (openingParenthesis < 0)
            return string.Empty;

        var depth = 0;
        for (var index = openingParenthesis; index < code.Length; index++)
        {
            if (code[index] == '(')
            {
                depth++;
                continue;
            }

            if (code[index] != ')')
                continue;

            depth--;
            if (depth == 0)
                return content[(openingParenthesis + 1)..index];
        }

        return content[(openingParenthesis + 1)..];
    }

    private static double ComputeIntegrity(
        double structured,
        double levels,
        double noise)
    {
        return Math.Round(
            structured * 0.50 +
            levels * 0.25 +
            noise * 0.25,
            2);
    }

    private sealed record ProjectIdentity(
        string Name,
        string Path,
        string ProjectKind,
        string? Framework);

    private sealed class ProjectLoggingScope(ProjectIdentity identity)
    {
        public ProjectIdentity Identity { get; } = identity;
        public List<ArchitectureEvaluatorResult> FileResults { get; } = [];
        public int AnalyzedFileCount { get; set; }
        public int LoggingFileCount { get; set; }
        public int FailedFileCount { get; set; }
    }

    private static double Average(
        IEnumerable<ArchitectureEvaluatorResult> results,
        string metric)
    {
        var values = results
            .Select(result =>
                result.Metrics.TryGetValue(metric, out var value)
                    ? value
                    : double.NaN)
            .Where(double.IsFinite)
            .ToArray();

        return values.Length == 0
            ? 0
            : Math.Round(values.Average(), 2);
    }

    private static string DetectLanguage(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            ".ts" or ".tsx" => "TypeScript",
            ".js" or ".jsx" => "JavaScript",
            _ => "Unknown"
        };
    }

    private IEnumerable<string> DetectFrameworks(string language)
    {
        return _policy.FrameworkHints.TryGetValue(
                language,
                out var frameworks)
            ? frameworks
            : Array.Empty<string>();
    }
}
