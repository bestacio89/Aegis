
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
                scope.Identity,
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
        ProjectIdentity identity,
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
                return $"Line {lineNumber}: {call.Value.Trim()} — {GetSourceLine(content, lineNumber)}";
            })
            .ToArray();

        var result = CreateResult(file, "Infrastructure");
        result.ProjectName = identity.Name;
        result.Language = language;
        result.Framework = identity.Framework;
        result.Layer = ResolveLayerForFile(file);
        result.Metrics["LoggingCallCount"] = loggingCalls.Count;
        result.Metrics["StructuredLoggingScore"] = structured;
        result.Metrics["LogLevelComplianceScore"] = levels;
        result.Metrics["LoggingNoiseScore"] = noise;
        result.Metrics["LoggingIntegrityScore"] = integrity;

        result.Metadata["FileName"] = Path.GetFileName(file);
        result.Metadata["FilePath"] = file;
        result.Metadata["ProjectName"] = identity.Name;
        result.Metadata["ProjectPath"] = identity.Path;
        result.Metadata["ProjectKind"] = identity.ProjectKind;
        result.Metadata["Language"] = language;
        result.Metadata["Framework"] = identity.Framework ?? "Unknown";
        result.Metadata["Layer"] = result.Layer ?? "Unknown";
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
            .SelectMany(result =>
            {
                var filePath = result.Metadata.GetValueOrDefault("FilePath");
                var relativePath = string.IsNullOrWhiteSpace(filePath)
                    ? result.Target
                    : Path.GetRelativePath(scope.Identity.Path, filePath);

                return (result.Evidence ?? string.Empty)
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => $"{relativePath}: {item}");
            })
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
