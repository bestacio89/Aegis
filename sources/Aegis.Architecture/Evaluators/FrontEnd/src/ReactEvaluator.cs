
using System.Diagnostics;
using System.Text.Json;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd.src;

public sealed class ReactEvaluator : BaseArchitectureEvaluator
{
    private const string AnalyzerFileName = "frontend-analyzer.mjs";
    private const string AnalyzerPathEnvironmentVariable =
        "AEGIS_FRONTEND_ANALYZER_PATH";
    private const string NodePathEnvironmentVariable =
        "AEGIS_NODE_EXECUTABLE";
    private const string TimeoutEnvironmentVariable =
        "AEGIS_FRONTEND_ANALYZER_TIMEOUT_SECONDS";

    private const int DefaultTimeoutSeconds = 120;
    private const int MaximumTimeoutSeconds = 1800;

    private readonly ILogger<ReactEvaluator> _logger;
    private readonly FrontendPolicy _policy;

    public ReactEvaluator(
        ILogger<ReactEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _policy = options.Value.Frontend ?? new FrontendPolicy();
    }

    public override string Name => nameof(ReactEvaluator);

    public override string[] SupportedLanguages =>
   [
       "TypeScript",
    "JavaScript"
   ];

    public override string[] SupportedFrameworks =>
    [
        "React"
    ];

 
        protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
    EvaluateCoreAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException(
                "A React project path is required.",
                nameof(projectPath));
        }

        string fullProjectPath;

        try
        {
            fullProjectPath = Path.GetFullPath(projectPath);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            _logger.LogWarning(
                exception,
                "Invalid React project path: {ProjectPath}",
                projectPath);

            return [CreateFailureResult(
                projectPath,
                "InvalidProjectPath",
                exception.Message)];
        }

        if (!Directory.Exists(fullProjectPath))
        {
            return [CreateFailureResult(
                fullProjectPath,
                "ProjectDirectoryNotFound",
                $"The project directory does not exist: {fullProjectPath}")];
        }

        var analyzerPath = ResolveAnalyzerPath(fullProjectPath);

        if (analyzerPath is null)
        {
            return [CreateFailureResult(
                fullProjectPath,
                "AnalyzerNotFound",
                $"Could not locate {AnalyzerFileName}. Configure " +
                $"{AnalyzerPathEnvironmentVariable} with its full path.")];
        }

        var nodeExecutable =
            Environment.GetEnvironmentVariable(NodePathEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(nodeExecutable))
        {
            nodeExecutable = "node";
        }

        var timeout = GetTimeout();

        _logger.LogInformation(
            "Starting React analysis for {ProjectPath} using {AnalyzerPath}.",
            fullProjectPath,
            analyzerPath);

        AnalyzerExecution execution;

        try
        {
            execution = await ExecuteAnalyzerAsync(
                nodeExecutable,
                analyzerPath,
                fullProjectPath,
                timeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "React analyzer execution failed for {ProjectPath}.",
                fullProjectPath);

            return [CreateFailureResult(
                fullProjectPath,
                "AnalyzerExecutionFailed",
                exception.Message)];
        }

        if (execution.TimedOut)
        {
            _logger.LogWarning(
                "React analysis timed out after {TimeoutSeconds} seconds " +
                "for {ProjectPath}.",
                timeout.TotalSeconds,
                fullProjectPath);

            return [CreateFailureResult(
                fullProjectPath,
                "AnalyzerTimeout",
                $"React analysis exceeded the timeout of " +
                $"{timeout.TotalSeconds} seconds.")];
        }

        if (execution.ExitCode != 0)
        {
            var diagnostic = LimitDiagnostic(
                string.IsNullOrWhiteSpace(execution.StandardError)
                    ? execution.StandardOutput
                    : execution.StandardError);

            _logger.LogWarning(
                "React analyzer exited with code {ExitCode} for {ProjectPath}.",
                execution.ExitCode,
                fullProjectPath);

            return [CreateFailureResult(
                fullProjectPath,
                "AnalyzerProcessFailed",
                $"Node.js exited with code {execution.ExitCode}. " +
                diagnostic)];
        }

        if (string.IsNullOrWhiteSpace(execution.StandardOutput))
        {
            return [CreateFailureResult(
                fullProjectPath,
                "EmptyAnalyzerOutput",
                "The React analyzer returned no JSON output.")];
        }

        try
        {
            using var document = JsonDocument.Parse(
                execution.StandardOutput);

            return MapAnalyzerOutput(
                fullProjectPath,
                document.RootElement,
                execution.StandardError);
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "React analyzer returned invalid JSON for {ProjectPath}.",
                fullProjectPath);

            return [CreateFailureResult(
                fullProjectPath,
                "InvalidAnalyzerJson",
                $"The analyzer output could not be parsed. {exception.Message}")];
        }
    }

    private IReadOnlyCollection<ArchitectureEvaluatorResult> MapAnalyzerOutput(
        string projectPath,
        JsonElement root,
        string standardError)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [CreateFailureResult(
                projectPath,
                "InvalidAnalyzerSchema",
                "The analyzer JSON root must be an object.")];
        }

        var analyzerStatus = GetString(root, "status");
        var framework = GetString(root, "framework");
        var analyzerVersion = GetString(root, "analyzerVersion");
        var parser = GetString(root, "parser");
        var compilerBacked = GetBoolean(root, "compilerBacked");

        if (!string.Equals(
                framework,
                "React",
                StringComparison.OrdinalIgnoreCase))
        {
            return [CreateFailureResult(
                projectPath,
                "UnexpectedFramework",
                $"Expected framework 'React' but received " +
                $"'{framework ?? "(missing)"}'.")];
        }

        if (!TryGetProperty(root, "files", out var files)
            || files.ValueKind != JsonValueKind.Array)
        {
            return [CreateFailureResult(
                projectPath,
                "InvalidAnalyzerSchema",
                "The analyzer response does not contain a valid files array.")];
        }

        var results = new List<ArchitectureEvaluatorResult>();
        var aggregate = new AggregateMetrics();

        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object)
            {
                aggregate.InvalidFileRecords++;
                continue;
            }

            var reportedPath = GetString(file, "filePath");

            if (string.IsNullOrWhiteSpace(reportedPath))
            {
                aggregate.InvalidFileRecords++;
                continue;
            }

            var filePath = ResolveReportedFilePath(
                projectPath,
                reportedPath);

            var result = CreateResult(filePath, "Frontend");

            result.Metadata[FilePathMetadataKey] = filePath;
            result.Metadata[EvaluationScopeMetadataKey] = FileScope;
            result.Metadata["Framework"] = "React";
            result.Metadata["AnalyzerVersion"] =
                analyzerVersion ?? "Unknown";
            result.Metadata["AnalysisStatus"] =
                analyzerStatus ?? "Unknown";

            AddStringMetadata(result, "Parser", parser);
            AddBooleanMetadata(result, "CompilerBacked", compilerBacked);

            var fileMetrics = new AggregateMetrics();

            if (TryGetNonNegativeInteger(
                    file,
                    "sourceLineCount",
                    out var sourceLineCount))
            {
                AddMetric(result, "SourceLineCount", sourceLineCount);
                fileMetrics.SourceLineCount = sourceLineCount;
            }

            if (TryGetArrayCount(file, "components", out var componentCount))
            {
                AddMetric(result, "ComponentCount", componentCount);
                fileMetrics.ComponentCount = componentCount;
            }

            if (TryGetArrayCount(file, "hooks", out var hookCount))
            {
                AddMetric(result, "HookCount", hookCount);
                fileMetrics.HookCount = hookCount;

                if (TryComputeHookDisciplineScore(file, out var hookDisciplineScore))
                {
                    AddMetric(result, "HookDisciplineScore", hookDisciplineScore);
                }
            }

            if (TryComputeTypingScore(file, out var typingScore))
            {
                AddMetric(result, "TypingScore", typingScore);
            }

            if (TryComputeComplexityScore(file, out var complexityScore))
            {
                AddMetric(result, "ComplexityScore", complexityScore);
            }

            if (TryGetNonNegativeInteger(
                    file,
                    "jsxElementCount",
                    out var jsxElementCount))
            {
                AddMetric(result, "JsxElementCount", jsxElementCount);
                fileMetrics.JsxElementCount = jsxElementCount;
            }

            if (TryGetNonNegativeInteger(
                    file,
                    "conditionalCount",
                    out var conditionalCount))
            {
                AddMetric(result, "ConditionalCount", conditionalCount);
                fileMetrics.ConditionalCount = conditionalCount;
            }

            if (TryGetCount(
                    file,
                    "syntaxDiagnostics",
                    out var syntaxDiagnosticCount))
            {
                AddMetric(
                    result,
                    "SyntaxDiagnosticCount",
                    syntaxDiagnosticCount);

                fileMetrics.SyntaxDiagnosticCount =
                    syntaxDiagnosticCount;
            }

            result.Metadata["FileName"] = Path.GetFileName(filePath);

            aggregate.Add(fileMetrics);
            results.Add(result);
        }

        var reportedFilesAnalyzed = TryGetNonNegativeInteger(
            root,
            "filesAnalyzed",
            out var filesAnalyzed)
                ? filesAnalyzed
                : results.Count;

        var summary = new ArchitectureEvaluatorResult(
            Name,
            Path.GetFileName(projectPath))
        {
            Category = "FrontendSummary"
        };

        summary.Metadata[EvaluationScopeMetadataKey] = ProjectScope;
        summary.Metadata["ProjectPath"] = projectPath;
        summary.Metadata["Framework"] = "React";
        summary.Metadata["AnalysisStatus"] =
            analyzerStatus ?? "Unknown";
        summary.Metadata["AnalyzerVersion"] =
            analyzerVersion ?? "Unknown";
        summary.Metadata["ReportedFilesAnalyzed"] = reportedFilesAnalyzed.ToString();
        summary.Metadata["FilesMapped"] = results.Count.ToString();
        summary.Metadata["InvalidFileRecords"] =
            aggregate.InvalidFileRecords.ToString();

        AddStringMetadata(summary, "Parser", parser);
        AddBooleanMetadata(summary, "CompilerBacked", compilerBacked);

        AddMetric(summary, "FilesAnalyzed", reportedFilesAnalyzed);
        AddMetric(summary, "FilesMapped", results.Count);
        AddMetric(summary, "ComponentCount", aggregate.ComponentCount);
        AddMetric(summary, "HookCount", aggregate.HookCount);
        AddMetric(summary, "SourceLineCount", aggregate.SourceLineCount);
        AddMetric(summary, "JsxElementCount", aggregate.JsxElementCount);
        AddMetric(summary, "ConditionalCount", aggregate.ConditionalCount);
        AddMetric(
            summary,
            "SyntaxDiagnosticCount",
            aggregate.SyntaxDiagnosticCount);
        AddMetric(
            summary,
            "InvalidFileRecordCount",
            aggregate.InvalidFileRecords);

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            summary.Metadata["AnalyzerStandardError"] =
                LimitDiagnostic(standardError);
        }

        results.Add(summary);

        _logger.LogInformation(
            "React analysis finished for {ProjectPath}. Status: {Status}; " +
            "reported files: {ReportedFiles}; mapped files: {MappedFiles}; " +
            "components: {Components}; hooks: {Hooks}.",
            projectPath,
            analyzerStatus ?? "Unknown",
            reportedFilesAnalyzed,
            results.Count - 1,
            aggregate.ComponentCount,
            aggregate.HookCount);

        if (!string.Equals(
                analyzerStatus,
                "completed",
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "React analysis for {ProjectPath} returned status {Status}.",
                projectPath,
                analyzerStatus ?? "(missing)");
        }

        return results;
    }

    private ArchitectureEvaluatorResult CreateFailureResult(
        string projectPath,
        string status,
        string diagnostic)
    {
        var result = new ArchitectureEvaluatorResult(
            Name,
            Path.GetFileName(projectPath))
        {
            Category = "FrontendSummary"
        };

        result.Metadata[EvaluationScopeMetadataKey] = ProjectScope;
        result.Metadata["ProjectPath"] = projectPath;
        result.Metadata["Framework"] = "React";
        result.Metadata["AnalysisStatus"] = status;
        result.Metadata["Diagnostic"] = LimitDiagnostic(diagnostic);

        _logger.LogWarning(
            "React analysis did not complete for {ProjectPath}: {Status}. " +
            "{Diagnostic}",
            projectPath,
            status,
            LimitDiagnostic(diagnostic));

        return result;
    }

    private static async Task<AnalyzerExecution> ExecuteAnalyzerAsync(
        string nodeExecutable,
        string analyzerPath,
        string projectPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = nodeExecutable,
            WorkingDirectory = projectPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(analyzerPath);
        startInfo.ArgumentList.Add("react");
        startInfo.ArgumentList.Add(projectPath);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        cancellationToken.ThrowIfCancellationRequested();

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Failed to start the Node.js analyzer process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            await WaitForExitAfterKillAsync(process).ConfigureAwait(false);

            var stdoutAfterTimeout = await stdoutTask.ConfigureAwait(false);
            var stderrAfterTimeout = await stderrTask.ConfigureAwait(false);

            return new AnalyzerExecution(
                process.HasExited ? process.ExitCode : -1,
                stdoutAfterTimeout,
                stderrAfterTimeout,
                TimedOut: true);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new AnalyzerExecution(
            process.ExitCode,
            stdout,
            stderr,
            TimedOut: false);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process has already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process could not be terminated. The caller will report
            // the execution failure rather than treating it as success.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // No process remains to wait for.
        }
    }

    private static string? ResolveAnalyzerPath(string projectPath)
    {
        var configuredPath = Environment.GetEnvironmentVariable(
            AnalyzerPathEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(
                configuredPath);

            if (File.Exists(expandedPath))
            {
                return Path.GetFullPath(expandedPath);
            }

            return null;
        }

        var roots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            projectPath
        };

        var visited = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var current = new DirectoryInfo(root);

            while (current is not null)
            {
                if (visited.Add(current.FullName))
                {
                    var directCandidate = Path.Combine(
                        current.FullName,
                        AnalyzerFileName);

                    if (File.Exists(directCandidate))
                    {
                        return directCandidate;
                    }

                    var repositoryCandidate = Path.Combine(
                        current.FullName,
                        "sources",
                        "Aegis.Architecture",
                        "Evaluators",
                        "FrontEnd",
                        AnalyzerFileName);

                    if (File.Exists(repositoryCandidate))
                    {
                        return repositoryCandidate;
                    }
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static TimeSpan GetTimeout()
    {
        var configuredValue = Environment.GetEnvironmentVariable(
            TimeoutEnvironmentVariable);

        if (int.TryParse(configuredValue, out var seconds)
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(
                Math.Min(seconds, MaximumTimeoutSeconds));
        }

        return TimeSpan.FromSeconds(DefaultTimeoutSeconds);
    }

    private static string ResolveReportedFilePath(
        string projectPath,
        string reportedPath)
    {
        try
        {
            var fullPath = Path.IsPathRooted(reportedPath)
                ? Path.GetFullPath(reportedPath)
                : Path.GetFullPath(
                    Path.Combine(projectPath, reportedPath));

            return fullPath;
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            // Preserve the analyzer's original path if it cannot be
            // normalized. This is evidence metadata, not a file-system write.
            return reportedPath;
        }
    }

    private static bool TryComputeHookDisciplineScore(
        JsonElement file,
        out int score)
    {
        score = 0;

        if (!TryGetProperty(file, "hooks", out var hooks)
            || hooks.ValueKind != JsonValueKind.Array
            || hooks.GetArrayLength() == 0)
        {
            return false;
        }

        var violations = 0;
        foreach (var hook in hooks.EnumerateArray())
        {
            if (hook.ValueKind != JsonValueKind.Object
                || !TryGetProperty(hook, "conditionallyCalled", out var conditional)
                || conditional.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                // Incomplete evidence must not become a pass or a violation.
                return false;
            }

            if (conditional.GetBoolean())
            {
                violations++;
            }
        }

        score = (int)Math.Round(
            (hooks.GetArrayLength() - violations) * 100d / hooks.GetArrayLength(),
            MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool TryComputeComplexityScore(
        JsonElement file,
        out int score)
    {
        score = 0;

        if (!TryGetProperty(file, "componentTyping", out var components)
            || components.ValueKind != JsonValueKind.Array
            || components.GetArrayLength() == 0)
        {
            return false;
        }

        var complexities = new List<int>();
        foreach (var component in components.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object
                || !TryGetProperty(component, "cyclomaticComplexity", out var complexity)
                || complexity.ValueKind != JsonValueKind.Number
                || !complexity.TryGetInt32(out var value)
                || value < 1)
            {
                return false;
            }

            complexities.Add(value);
        }

        // Score the worst component in the file so a complex component cannot
        // be hidden by averaging it with trivial components.
        score = Math.Max(0, 100 - ((complexities.Max() - 1) * 10));
        return true;
    }

    private static bool TryComputeTypingScore(
        JsonElement file,
        out int score)
    {
        score = 0;

        if (!TryGetProperty(file, "componentTyping", out var components)
            || components.ValueKind != JsonValueKind.Array
            || components.GetArrayLength() == 0)
        {
            return false;
        }

        var typedCount = 0;
        var total = 0;

        foreach (var component in components.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object
                || !TryGetProperty(component, "propsTyped", out var typed)
                || typed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            total++;
            if (typed.GetBoolean())
            {
                typedCount++;
            }
        }

        if (total == 0)
        {
            return false;
        }

        score = (int)Math.Round(
            typedCount * 100d / total,
            MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool TryGetProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(
        JsonElement element,
        string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool? GetBoolean(
        JsonElement element,
        string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True
            or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        if (value.ValueKind == JsonValueKind.String
            && bool.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryGetNonNegativeInteger(
        JsonElement element,
        string name,
        out int value)
    {
        value = 0;

        return TryGetProperty(element, name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value)
            && value >= 0;
    }

    private static bool TryGetArrayCount(
        JsonElement element,
        string name,
        out int count)
    {
        count = 0;

        if (!TryGetProperty(element, name, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        count = property.GetArrayLength();
        return true;
    }

    private static bool TryGetCount(
        JsonElement element,
        string name,
        out int count)
    {
        if (TryGetArrayCount(element, name, out count))
        {
            return true;
        }

        if (TryGetNonNegativeInteger(element, name, out count))
        {
            return true;
        }

        count = 0;
        return false;
    }

    private static void AddMetric(
        ArchitectureEvaluatorResult result,
        string name,
        int value)
    {
        result.Metrics[name] = value;
    }

    private static void AddStringMetadata(
        ArchitectureEvaluatorResult result,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            result.Metadata[name] = value;
        }
    }

    private static void AddBooleanMetadata(
        ArchitectureEvaluatorResult result,
        string name,
        bool? value)
    {
        if (value.HasValue)
        {
            result.Metadata[name] = value.Value.ToString();
        }
    }

    private static string LimitDiagnostic(string? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return "No additional diagnostic was provided.";
        }

        const int maximumLength = 4000;

        var normalized = diagnostic.Trim();

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength] + "…";
    }

    private sealed record AnalyzerExecution(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        bool TimedOut);

    private sealed class AggregateMetrics
    {
        public int ComponentCount { get; set; }
        public int HookCount { get; set; }
        public int SourceLineCount { get; set; }
        public int JsxElementCount { get; set; }
        public int ConditionalCount { get; set; }
        public int SyntaxDiagnosticCount { get; set; }
        public int InvalidFileRecords { get; set; }

        public void Add(AggregateMetrics other)
        {
            ComponentCount += other.ComponentCount;
            HookCount += other.HookCount;
            SourceLineCount += other.SourceLineCount;
            JsxElementCount += other.JsxElementCount;
            ConditionalCount += other.ConditionalCount;
            SyntaxDiagnosticCount += other.SyntaxDiagnosticCount;
        }
    }
}
