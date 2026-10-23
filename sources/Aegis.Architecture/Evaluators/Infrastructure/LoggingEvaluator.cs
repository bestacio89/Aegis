
using System.Text.RegularExpressions;

using Aegis.Architecture.Analysis.Detectors;

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
            _logger.LogInformation(
                "{Evaluator} is disabled by policy.",
                Name);

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

        var sourceFiles = EnumerateSourceFiles(projectPath, token);

        var analyzedFileCount = 0;
        var loggingFileCount = 0;
        var failedFileCount = 0;

        foreach (var file in sourceFiles)
        {
            token.ThrowIfCancellationRequested();
            analyzedFileCount++;

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
                failedFileCount++;

                _logger.LogDebug(
                    exception,
                    "Unable to read source file {FilePath}.",
                    file);

                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                failedFileCount++;

                _logger.LogDebug(
                    exception,
                    "Access denied while reading source file {FilePath}.",
                    file);

                continue;
            }

            var language = DetectLanguage(file);
            var loggingCalls = LoggingCallRx.Matches(content);

            // Files without recognizable logging calls do not contribute
            // artificial zeroes or perfect scores to logging-quality averages.
            if (loggingCalls.Count == 0)
            {
                continue;
            }

            loggingFileCount++;

            var structuredScore =
                EvaluateStructuredLogging(content, language);

            var levelScore =
                EvaluateLogLevels(content);

            var noiseScore =
                EvaluateLoggingNoise(content, language);

            var integrityScore =
                ComputeIntegrity(
                    structuredScore,
                    levelScore,
                    noiseScore);

            results.Add(
                CreateFileResult(
                    file,
                    language,
                    loggingCalls.Count,
                    structuredScore,
                    levelScore,
                    noiseScore,
                    integrityScore));
        }

        if (results.Count > 0)
        {
            results.Add(
                CreateSummaryResult(
                    projectPath,
                    results.ToArray(),
                    analyzedFileCount,
                    loggingFileCount,
                    failedFileCount));
        }
        else
        {
            results.Add(
                CreateEmptySummaryResult(
                    projectPath,
                    analyzedFileCount,
                    loggingFileCount,
                    failedFileCount));
        }

        _logger.LogInformation(
            "{Evaluator} completed. Source files: {SourceFiles}; files with logging calls: {LoggingFiles}; unreadable files: {FailedFiles}; result entries: {ResultCount}.",
            Name,
            analyzedFileCount,
            loggingFileCount,
            failedFileCount,
            results.Count);

        return results;
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string file,
        string language,
        int loggingCallCount,
        double structured,
        double levels,
        double noise,
        double integrity)
    {
        return new ArchitectureEvaluatorResult(Name, file)
        {
            Category = "Infrastructure",

            Metrics =
            {
                ["LoggingCallCount"] = loggingCallCount,
                ["StructuredLoggingScore"] = structured,
                ["LogLevelComplianceScore"] = levels,
                ["LoggingNoiseScore"] = noise,
                ["LoggingIntegrityScore"] = integrity
            },

            Metadata =
            {
                ["FileName"] = Path.GetFileName(file),
                ["Target"] = file,
                ["Language"] = language,
                ["Frameworks"] = string.Join(
                    ", ",
                    DetectFrameworks(language)),
                ["PolicyEnabled"] = _policy.Enabled.ToString(),
                ["EvaluationScope"] = FileScope,
            }
        };
    }

    private ArchitectureEvaluatorResult CreateSummaryResult(
        string projectPath,
        IReadOnlyCollection<ArchitectureEvaluatorResult> results,
        int analyzedFileCount,
        int loggingFileCount,
        int failedFileCount)
    {
        var integrity = Average(results, "LoggingIntegrityScore");
        var structured = Average(results, "StructuredLoggingScore");
        var levels = Average(results, "LogLevelComplianceScore");
        var noise = Average(results, "LoggingNoiseScore");

        return new ArchitectureEvaluatorResult(Name, projectPath)
        {
            Category = "InfrastructureSummary",

            Metrics =
            {
                ["SourceFilesScanned"] = analyzedFileCount,
                ["FilesWithLoggingCalls"] = loggingFileCount,
                ["FilesWithLoggingResults"] = results.Count,
                ["UnreadableFiles"] = failedFileCount,
                ["DiagnosticCount"] = failedFileCount,
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
                ["Evaluator"] = Name,
                ["PolicyEnabled"] = _policy.Enabled.ToString(),
                ["SupportedFrameworks"] = string.Join(
                    ", ",
                    SupportedFrameworks),
                ["AnalysisComplete"] = (failedFileCount == 0).ToString(),
                ["AnalysisStatus"] = failedFileCount == 0
                    ? "Completed"
                    : "CompletedWithDiagnostics",
                ["Diagnostics"] = failedFileCount == 0
                    ? string.Empty
                    : $"Unable to read {failedFileCount} source file(s) during logging analysis.",
                ["ScoringNote"] =
                    "Scores are heuristic indicators based on files containing recognized logging calls."
            }
        };
    }

    private ArchitectureEvaluatorResult CreateEmptySummaryResult(
        string projectPath,
        int analyzedFileCount,
        int loggingFileCount,
        int failedFileCount)
    {
        return new ArchitectureEvaluatorResult(Name, projectPath)
        {
            Category = "InfrastructureSummary",

            Metrics =
            {
                ["SourceFilesScanned"] = analyzedFileCount,
                ["FilesWithLoggingCalls"] = loggingFileCount,
                ["FilesWithLoggingResults"] = 0,
                ["UnreadableFiles"] = failedFileCount,
                ["DiagnosticCount"] = failedFileCount
            },

            Metadata =
            {
                ["Evaluator"] = Name,
                ["PolicyEnabled"] = _policy.Enabled.ToString(),
                ["EvaluationStatus"] =
                    analyzedFileCount == 0
                        ? "NoSourceFilesFound"
                        : loggingFileCount == 0
                            ? "NoRecognizedLoggingCalls"
                            : "NoFilesEvaluated",
                ["AnalysisComplete"] = (failedFileCount == 0).ToString(),
                ["AnalysisStatus"] = failedFileCount == 0
                    ? "Completed"
                    : "CompletedWithDiagnostics",
                ["Diagnostics"] = failedFileCount == 0
                    ? string.Empty
                    : $"Unable to read {failedFileCount} source file(s) during logging analysis.",
                ["EvaluationScope"] = ProjectScope
            }
        };
    }

    private double EvaluateStructuredLogging(
        string content,
        string language)
    {
        if (!_policy.RequireStructuredLogging)
        {
            return 100;
        }

        var calls = LoggingCallRx.Matches(content);

        if (calls.Count == 0)
        {
            return 100;
        }

        var structuredCalls = 0;
        var parameterizedCalls = 0;
        var interpolatedCalls = 0;

        foreach (Match call in calls)
        {
            var windowLength = Math.Min(
                500,
                content.Length - call.Index);

            if (windowLength <= 0)
            {
                continue;
            }

            var window = content.Substring(call.Index, windowLength);

            // These checks are intentionally conservative heuristics:
            // AST-backed analysis would be required to associate every
            // message template and argument list with complete accuracy.
            var hasTemplate = StructuredTemplateRx.IsMatch(window);
            var hasPythonParameters =
                language == "Python" &&
                PythonParameterizedLoggingRx.IsMatch(window);

            var hasInterpolation =
                InterpolatedStringRx.IsMatch(window);

            if (hasTemplate)
            {
                structuredCalls++;
            }
            else if (hasPythonParameters)
            {
                parameterizedCalls++;
            }
            else if (hasInterpolation)
            {
                interpolatedCalls++;
            }
        }

        // Structured message templates and parameterized Python logging
        // are positive evidence. Interpolation is not counted as structured.
        var positiveCalls = structuredCalls + parameterizedCalls;

        return Math.Round(
            100.0 * positiveCalls / calls.Count,
            2);
    }

    private double EvaluateLogLevels(string content)
    {
        if (!_policy.RequireLogLevels)
        {
            return 100;
        }

        var calls = LogLevelRx.Matches(content);

        if (calls.Count == 0)
        {
            // The caller already established that a recognizable logging
            // call exists; no recognized level-specific call is neutral
            // evidence rather than proof of compliance.
            return 50;
        }

        var recognizedLevelCalls = calls.Count;

        return Math.Round(
            100.0 * recognizedLevelCalls / calls.Count,
            2);
    }

    private double EvaluateLoggingNoise(
        string content,
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
            var windowLength = Math.Min(
                500,
                content.Length - call.Index);

            if (windowLength <= 0)
            {
                continue;
            }

            var window = content.Substring(call.Index, windowLength);

            var interpolated =
                InterpolatedStringRx.IsMatch(window);

            var pythonPercentFormatting =
                language == "Python" &&
                PythonParameterizedLoggingRx.IsMatch(window);

            if (interpolated && !pythonPercentFormatting)
            {
                interpolatedCalls++;
            }
        }

        var ratio = (double)interpolatedCalls / calls.Count;

        return Math.Round(
            Math.Clamp(100.0 * (1.0 - ratio), 0, 100),
            2);
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

    private static List<string> EnumerateSourceFiles(
        string projectPath,
        CancellationToken token)
    {
        var files = new List<string>();
        var pendingDirectories = new Stack<string>();

        pendingDirectories.Push(Path.GetFullPath(projectPath));

        while (pendingDirectories.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var currentDirectory = pendingDirectories.Pop();
            if (FileExclusions.IsExcluded(currentDirectory))
                continue;

            IEnumerable<string> childDirectories;

            try
            {
                childDirectories = Directory.EnumerateDirectories(
                    currentDirectory).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var directory in childDirectories)
            {
                token.ThrowIfCancellationRequested();

                if (FileExclusions.IsExcluded(directory))
                    continue;

                pendingDirectories.Push(directory);
            }

            IEnumerable<string> currentFiles;

            try
            {
                currentFiles = Directory.EnumerateFiles(
                    currentDirectory).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            files.AddRange(currentFiles
                .Where(IsSupportedSourceFile)
                .Where(file => !FileExclusions.IsExcluded(file)));
        }

        return files;
    }

    private static bool IsSupportedSourceFile(string file)
    {
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => true,
            ".java" => true,
            ".py" => true,
            ".ts" => true,
            ".tsx" => true,
            ".js" => true,
            ".jsx" => true,
            _ => false
        };
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
