using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.FrontEnd;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.FrontEnd.src;

/// <summary>
/// Evaluates Angular projects through the dedicated Node.js frontend analyzer.
///
/// Responsibilities:
/// - Locate and execute frontend-analyzer.mjs.
/// - Validate the analyzer response.
/// - Translate verified analysis facts into Aegis metrics and metadata.
/// - Preserve analysis coverage and capability limitations.
///
/// This evaluator does not independently parse TypeScript or Angular templates.
/// It does not claim that parsing constitutes type checking or compilation.
/// </summary>
public sealed class AngularEvaluator : BaseArchitectureEvaluator
{
    private const string AnalyzerFileName = "frontend-analyzer.mjs";
    private const string FrameworkName = "Angular";
    private const string LanguageName = "TypeScript";

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 128
    };

    private readonly FrontendPolicy _policy;
    private readonly string? _configuredAnalyzerPath;
    private readonly string _nodeExecutable;
    private readonly TimeSpan _processTimeout;

    public override string Name => nameof(AngularEvaluator);

    public override string[] SupportedLanguages => [LanguageName];

    public override string[] SupportedFrameworks => [FrameworkName];

    public AngularEvaluator(
        ILogger<AngularEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        var policy = options.Value.Frontend;

        _policy = policy ?? new FrontendPolicy();

        // Optional configuration is resolved without requiring a new
        // policy property, so existing policy configuration remains valid.
        _configuredAnalyzerPath =
            Environment.GetEnvironmentVariable(
                "AEGIS_FRONTEND_ANALYZER_PATH");

        _nodeExecutable =
            Environment.GetEnvironmentVariable(
                "AEGIS_NODE_EXECUTABLE") ?? "node";

        var timeoutValue =
            Environment.GetEnvironmentVariable(
                "AEGIS_FRONTEND_ANALYZER_TIMEOUT_SECONDS");

        _processTimeout =
            int.TryParse(
                timeoutValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var timeoutSeconds) &&
            timeoutSeconds > 0
                ? TimeSpan.FromSeconds(timeoutSeconds)
                : TimeSpan.FromMinutes(5);
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        token.ThrowIfCancellationRequested();

        var fullProjectPath = Path.GetFullPath(projectPath);

        if (!Directory.Exists(fullProjectPath))
        {
            return
            [
                CreateProjectFailure(
                    fullProjectPath,
                    "ProjectDirectoryNotFound",
                    $"The project directory does not exist: {fullProjectPath}")
            ];
        }

        var analyzerPath = ResolveAnalyzerPath();

        if (analyzerPath is null)
        {
            _logger.LogError(
                "Unable to locate {AnalyzerFileName}. Configure " +
                "AEGIS_FRONTEND_ANALYZER_PATH or deploy the analyzer " +
                "alongside Aegis.",
                AnalyzerFileName);

            return
            [
                CreateProjectFailure(
                    fullProjectPath,
                    "AnalyzerNotFound",
                    $"Unable to locate {AnalyzerFileName}.")
            ];
        }

        var execution = await ExecuteAnalyzerAsync(
            analyzerPath,
            fullProjectPath,
            token);

        token.ThrowIfCancellationRequested();

        if (!execution.Started)
        {
            return
            [
                CreateProjectFailure(
                    fullProjectPath,
                    "AnalyzerProcessStartFailed",
                    execution.Error ?? "The Node.js process could not start.")
            ];
        }

        if (execution.TimedOut)
        {
            return
            [
                CreateProjectFailure(
                    fullProjectPath,
                    "AnalyzerTimedOut",
                    $"The frontend analyzer exceeded the timeout of " +
                    $"{_processTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.")
            ];
        }

        if (execution.ExitCode != 0)
        {
            var error = LimitText(
                execution.StandardError,
                4_000);

            if (string.IsNullOrWhiteSpace(error))
            {
                error = $"The analyzer exited with code {execution.ExitCode}.";
            }

            _logger.LogError(
                "Angular analyzer failed with exit code {ExitCode}: {Error}",
                execution.ExitCode,
                error);

            return
            [
                CreateProjectFailure(
                    fullProjectPath,
                    "AnalyzerExecutionFailed",
                    error,
                    execution.ExitCode)
            ];
        }

        if (string.IsNullOrWhiteSpace(execution.StandardOutput))
        {
            return
            [
                CreateProjectFailure(
                    fullProjectPath,
                    "AnalyzerReturnedEmptyOutput",
                    "The analyzer exited successfully but returned no JSON.")
            ];
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(
                execution.StandardOutput,
                JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Angular analyzer returned invalid JSON for {ProjectPath}",
                fullProjectPath);

            return
            [
                CreateProjectFailure(
                    fullProjectPath,
                    "AnalyzerReturnedInvalidJson",
                    ex.Message)
            ];
        }

        using (document)
        {
            return TranslateAnalysis(
                fullProjectPath,
                document.RootElement,
                execution.StandardError);
        }
    }

    private IEnumerable<ArchitectureEvaluatorResult> TranslateAnalysis(
        string projectPath,
        JsonElement root,
        string standardError)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return
            [
                CreateProjectFailure(
                    projectPath,
                    "InvalidAnalyzerResponse",
                    "The analyzer response root must be a JSON object.")
            ];
        }

        var status = GetString(root, "status") ?? "unknown";

        if (!IsSuccessfulStatus(status))
        {
            var error = GetString(root, "error")
                ?? GetString(root, "message")
                ?? $"The analyzer reported status '{status}'.";

            return
            [
                CreateProjectFailure(
                    projectPath,
                    "AnalyzerReportedFailure",
                    error)
            ];
        }

        var results = new List<ArchitectureEvaluatorResult>();

        var filesElement = GetProperty(
            root,
            "files",
            "analyzedFiles");

        var files = filesElement is { ValueKind: JsonValueKind.Array }
            ? filesElement.Value.EnumerateArray().ToArray()
            : [];

        var reportedFilesAnalyzed = GetInt(
            root,
            "filesAnalyzed",
            "analyzedFileCount");

        var filesAnalyzed = reportedFilesAnalyzed
            ?? files.Length;

        var componentCount = 0;
        var standaloneCount = 0;
        var knownStandaloneCount = 0;
        var moduleCount = 0;
        var filesWithComponents = 0;
        var filesWithModules = 0;

        var diagnostics = GetStringArray(
            root,
            "diagnostics",
            "warnings");

        var angularVersion = GetString(
            root,
            "angularVersion",
            "declaredAngularVersion");

        var capabilities = GetProperty(
            root,
            "capabilities",
            "analysisCapabilities");

        foreach (var file in files)
        {
            if (file.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var filePath = GetString(
                file,
                "path",
                "filePath",
                "file",
                "relativePath");

            if (string.IsNullOrWhiteSpace(filePath))
            {
                // Preserve evidence without inventing a source path.
                filePath = $"AngularFile-{results.Count + 1}";
            }

            var componentsElement = GetProperty(
                file,
                "components");

            var components =
                componentsElement is
                {
                    ValueKind: JsonValueKind.Array
                }
                    ? componentsElement.Value
                        .EnumerateArray()
                        .ToArray()
                    : [];

            var modulesElement = GetProperty(
                file,
                "modules",
                "ngModules");

            var modules =
                modulesElement is
                {
                    ValueKind: JsonValueKind.Array
                }
                    ? modulesElement.Value
                        .EnumerateArray()
                        .ToArray()
                    : [];

            var reportedModuleCount = GetInt(
                file,
                "moduleCount",
                "ngModuleCount");

            var fileModuleCount = reportedModuleCount
                ?? modules.Length;

            componentCount += components.Length;
            moduleCount += fileModuleCount;

            if (components.Length > 0)
            {
                filesWithComponents++;
            }

            if (fileModuleCount > 0)
            {
                filesWithModules++;
            }

            var fileStandaloneCount = 0;
            var fileKnownStandaloneCount = 0;

            foreach (var component in components)
            {
                var standalone = GetBoolean(
                    component,
                    "standalone",
                    "isStandalone");

                if (standalone.HasValue)
                {
                    fileKnownStandaloneCount++;

                    if (standalone.Value)
                    {
                        fileStandaloneCount++;
                    }
                }
            }

            standaloneCount += fileStandaloneCount;
            knownStandaloneCount += fileKnownStandaloneCount;

            var metrics = new Dictionary<string, double>
            {
                ["AngularComponentCount"] = components.Length,
                ["AngularModuleCount"] = fileModuleCount
            };

            // These rule-facing scores are emitted only when the analyzer
            // provides the component-level evidence required to calculate them.
            if (TryComputeSelectorScore(components, out var selectorScore))
            {
                metrics["SelectorScore"] = selectorScore;
            }

            var componentComplexities = components
                .Select(component => GetInt(component, "cyclomaticComplexity"))
                .ToArray();

            if (components.Length > 0 && componentComplexities.All(value => value.HasValue))
            {
                var maximumComplexity = componentComplexities
                    .Select(value => value!.Value)
                    .DefaultIfEmpty(1)
                    .Max();

                // A score of 100 represents a single-path component. Each
                // additional independent path reduces the score by ten points.
                metrics["ComplexityScore"] = Math.Max(
                    0,
                    100 - ((maximumComplexity - 1) * 10));
            }

            var metadata = new Dictionary<string, string>
            {
                [BaseArchitectureEvaluator.FilePathMetadataKey] = filePath,
                [BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
                    BaseArchitectureEvaluator.FileScope,
                ["Framework"] = FrameworkName,
                ["Language"] = LanguageName,
                ["AnalysisStatus"] = status,
                ["Analyzer"] = AnalyzerFileName,
                ["SemanticAnalysisPerformed"] = "False",
                ["ComponentDetailsAvailable"] =
                    componentsElement.HasValue.ToString(),
                ["ModuleDetailsAvailable"] =
                    modulesElement.HasValue.ToString(),
                ["StandaloneComponentCountKnown"] =
                    fileKnownStandaloneCount.ToString(
                        CultureInfo.InvariantCulture),
                ["StandaloneComponentCount"] =
                    fileStandaloneCount.ToString(
                        CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrWhiteSpace(angularVersion))
            {
                metadata["DeclaredAngularVersion"] = angularVersion;
            }

            if (components.Length > 0)
            {
                metadata["ComponentNames"] = string.Join(
                    ",",
                    components.Select(GetComponentName)
                        .Where(name => !string.IsNullOrWhiteSpace(name)));
            }

            results.Add(
                new ArchitectureEvaluatorResult(Name, filePath)
                {
                    Category = "Frontend",
                    Metrics = metrics,
                    Metadata = metadata
                });
        }

        var summary = new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            Category = "FrontendSummary",
            Metrics =
            {
                ["AngularFileCount"] = filesAnalyzed,
                ["AngularComponentCount"] = componentCount,
                ["AngularStandaloneComponentCount"] = standaloneCount,
                ["AngularStandaloneStatusKnownCount"] = knownStandaloneCount,
                ["AngularModuleCount"] = moduleCount,
                ["FilesWithComponents"] = filesWithComponents,
                ["FilesWithModules"] = filesWithModules
            },
            Metadata =
            {
                [BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
                    BaseArchitectureEvaluator.ProjectScope,
                ["Framework"] = FrameworkName,
                ["Language"] = LanguageName,
                ["AnalysisStatus"] = status,
                ["Analyzer"] = AnalyzerFileName,
                ["AnalyzerExitStatus"] = "Success",
                ["SemanticAnalysisPerformed"] = "False",
                ["TypeScriptTypeCheckingPerformed"] =
                    GetCapability(capabilities, "typescriptTypeChecking"),
                ["AngularTemplateParsingPerformed"] =
                    GetCapability(capabilities, "angularTemplateParsing"),
                ["AngularTemplateTypeCheckingPerformed"] =
                    GetCapability(capabilities, "angularTemplateTypeChecking"),
                ["AngularProjectCompilationPerformed"] =
                    GetCapability(capabilities, "angularProjectCompilation"),
                ["StandaloneStatusCoverage"] =
                    knownStandaloneCount == componentCount
                        ? "Complete"
                        : knownStandaloneCount == 0
                            ? "Unavailable"
                            : "Partial"
            }
        };

        if (!string.IsNullOrWhiteSpace(angularVersion))
        {
            summary.Metadata["DeclaredAngularVersion"] = angularVersion;
        }

        if (diagnostics.Count > 0)
        {
            summary.Metadata["AnalyzerDiagnostics"] =
                LimitText(string.Join(" | ", diagnostics), 4_000);
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            // Stderr can contain warnings even when execution succeeds.
            summary.Metadata["AnalyzerStandardError"] =
                LimitText(standardError, 4_000);
        }

        results.Add(summary);

        _logger.LogInformation(
            "{Evaluator} completed: {Files} files, {Components} components, " +
            "{Modules} modules, status {Status}",
            Name,
            filesAnalyzed,
            componentCount,
            moduleCount,
            status);

        return results;
    }

    private async Task<AnalyzerExecutionResult> ExecuteAnalyzerAsync(
        string analyzerPath,
        string projectPath,
        CancellationToken token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _nodeExecutable,
            WorkingDirectory = Path.GetDirectoryName(analyzerPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Use ArgumentList to avoid quoting and injection problems
        // with paths containing spaces.
        startInfo.ArgumentList.Add(analyzerPath);
        startInfo.ArgumentList.Add("angular");
        startInfo.ArgumentList.Add(projectPath);

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                return AnalyzerExecutionResult.StartFailure(
                    "Process.Start returned false.");
            }
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException or
            UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "Unable to start Node.js executable {NodeExecutable}",
                _nodeExecutable);

            return AnalyzerExecutionResult.StartFailure(ex.Message);
        }

        // Drain both redirected streams concurrently to prevent the
        // child process from blocking on a full pipe.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = new CancellationTokenSource(
            _processTimeout);

        using var linkedCts = CancellationTokenSource
            .CreateLinkedTokenSource(token, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return new AnalyzerExecutionResult(
                Started: true,
                TimedOut: false,
                ExitCode: process.ExitCode,
                StandardOutput: stdout,
                StandardError: stderr,
                Error: null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKillProcessTree(process);

            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // The process may already have exited.
            }

            return new AnalyzerExecutionResult(
                Started: true,
                TimedOut: true,
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                Error: "Analyzer execution timed out.");
        }
    }

    private string? ResolveAnalyzerPath()
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(_configuredAnalyzerPath))
        {
            candidates.Add(_configuredAnalyzerPath);
        }

        // Deployment options: copy the script to the application output
        // directory or deploy it in a dedicated tools subdirectory.
        candidates.Add(
            Path.Combine(AppContext.BaseDirectory, AnalyzerFileName));

        candidates.Add(
            Path.Combine(
                AppContext.BaseDirectory,
                "tools",
                AnalyzerFileName));

        // Development layout: the evaluator assembly may be loaded from
        // the Aegis repository's build output.
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        for (var depth = 0; current is not null && depth < 10; depth++)
        {
            candidates.Add(
                Path.Combine(
                    current.FullName,
                    "sources",
                    "Aegis.Architecture",
                    "Evaluators",
                    "FrontEnd",
                    AnalyzerFileName));

            current = current.Parent;
        }

        foreach (var candidate in candidates.Distinct(
                     StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);

                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch (ArgumentException)
            {
                // Ignore malformed optional configuration paths.
            }
            catch (NotSupportedException)
            {
                // Ignore unsupported optional configuration paths.
            }
        }

        return null;
    }

    private ArchitectureEvaluatorResult CreateProjectFailure(
        string projectPath,
        string failureCode,
        string message,
        int? exitCode = null)
    {
        var result = new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            Category = "FrontendAnalysisStatus",
            Metadata =
            {
                [BaseArchitectureEvaluator.EvaluationScopeMetadataKey] =
                    BaseArchitectureEvaluator.ProjectScope,
                ["Framework"] = FrameworkName,
                ["Language"] = LanguageName,
                ["AnalysisStatus"] = "Failed",
                ["FailureCode"] = failureCode,
                ["FailureMessage"] = LimitText(message, 4_000),
                ["Analyzer"] = AnalyzerFileName,
                ["SemanticAnalysisPerformed"] = "False",
                ["ComplianceScoreApplicable"] = "False"
            }
        };

        if (exitCode.HasValue)
        {
            result.Metadata["AnalyzerExitCode"] =
                exitCode.Value.ToString(CultureInfo.InvariantCulture);
        }

        _logger.LogWarning(
            "{Evaluator} could not complete analysis for {ProjectPath}: " +
            "{FailureCode} - {Message}",
            Name,
            projectPath,
            failureCode,
            message);

        return result;
    }

    private static bool IsSuccessfulStatus(string status)
    {
        return status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("success", StringComparison.OrdinalIgnoreCase)
            || status.Equals("succeeded", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement? GetProperty(
        JsonElement element,
        params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                return value;
            }
        }

        // Accept camelCase, PascalCase, and other casing variations.
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (property.Name.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }
            }
        }

        return null;
    }

    private static string? GetString(
        JsonElement element,
        params string[] names)
    {
        var value = GetProperty(element, names);

        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? GetInt(
        JsonElement element,
        params string[] names)
    {
        var value = GetProperty(element, names);

        if (!value.HasValue)
        {
            return null;
        }

        if (value.Value.ValueKind == JsonValueKind.Number &&
            value.Value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.Value.ValueKind == JsonValueKind.String &&
            int.TryParse(
                value.Value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number))
        {
            return number;
        }

        return null;
    }

    private static bool? GetBoolean(
        JsonElement element,
        params string[] names)
    {
        var value = GetProperty(element, names);

        if (!value.HasValue)
        {
            return null;
        }

        if (value.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.Value.GetBoolean();
        }

        if (value.Value.ValueKind == JsonValueKind.String &&
            bool.TryParse(value.Value.GetString(), out var boolean))
        {
            return boolean;
        }

        return null;
    }

    private static List<string> GetStringArray(
        JsonElement element,
        params string[] names)
    {
        var value = GetProperty(element, names);

        if (!value.HasValue ||
            value.Value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.Value
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : null)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
    }

    private static string GetComponentName(JsonElement component)
    {
        return GetString(
            component,
            "className",
            "name",
            "componentName",
            "class") ?? string.Empty;
    }

    private bool TryComputeSelectorScore(
        IReadOnlyCollection<JsonElement> components,
        out double score)
    {
        score = default;

        var selectors = components
            .Select(component => GetString(component, "selector"))
            .Where(selector => !string.IsNullOrWhiteSpace(selector))
            .Select(selector => selector!)
            .ToArray();

        if (selectors.Length == 0)
        {
            return false;
        }

        var prefixes = (_policy.AllowedSelectorPrefixes ?? [])
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(Regex.Escape)
            .ToArray();

        if (prefixes.Length == 0)
        {
            return false;
        }

        var allowedPrefix = string.Join("|", prefixes);
        var simpleSelector = new Regex(
            @"^(?:[a-z][a-z0-9]*(?:-[a-z0-9]+)*|\\[[A-Za-z][A-Za-z0-9]*\\])$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        var elementSelector = new Regex(
            $@"^(?:{allowedPrefix})-[a-z][a-z0-9]*(?:-[a-z0-9]+)*$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        var attributeSelector = new Regex(
            $@"^\\[(?:{allowedPrefix})[A-Z][A-Za-z0-9]*\\]$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        // Avoid inventing a score for compound CSS selectors that this policy
        // does not explicitly understand. Such files remain unmeasured.
        if (selectors.Any(selector => !simpleSelector.IsMatch(selector)))
        {
            return false;
        }

        var compliant = selectors.Count(selector =>
            elementSelector.IsMatch(selector) ||
            attributeSelector.IsMatch(selector));

        score = Math.Round(compliant * 100d / selectors.Length, 2);
        return true;
    }

    private static string GetCapability(
        JsonElement? capabilities,
        string name)
    {
        if (!capabilities.HasValue)
        {
            return "Unknown";
        }

        var value = GetBoolean(capabilities.Value, name);

        return value switch
        {
            true => "True",
            false => "False",
            null => "Unknown"
        };
    }

    private static string LimitText(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
        {
            return value;
        }

        return value[..maximumLength] + "…";
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
            // The operating system refused the termination request.
        }
    }

    private sealed record AnalyzerExecutionResult(
        bool Started,
        bool TimedOut,
        int? ExitCode,
        string StandardOutput,
        string StandardError,
        string? Error)
    {
        public static AnalyzerExecutionResult StartFailure(string error)
        {
            return new AnalyzerExecutionResult(
                Started: false,
                TimedOut: false,
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                Error: error);
        }
    }
}
