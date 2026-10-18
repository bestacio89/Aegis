
using System.Text.Json;
using System.Text.RegularExpressions;
using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Aegis.Architecture.Evaluators.Infrastructure;

public sealed class ConfigurationEvaluator : BaseArchitectureEvaluator
{
    private const string FileScope = "File";
    private const string ProjectScope = "Project";

    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".svn",
            ".hg",
            "node_modules",
            "bin",
            "obj",
            "dist",
            "build",
            "coverage",
            ".next",
            ".nuxt",
            "vendor",
            "packages",
            "TestResults",
            ".idea",
            ".vs"
        };

    private static readonly Regex UnsafeDockerCommandRegex = new(
        @"(?im)
        ^\s*
        (?:RUN|CMD|ENTRYPOINT|SHELL)\s+
        .*?
        (?:
            chmod\s+0?777\b|
            curl\b[^\r\n|]*\|\s*(?:sh|bash)\b|
            wget\b[^\r\n|]*\|\s*(?:sh|bash)\b|
            rm\s+-rf\s+/(?:\s|$)
        )",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex PrivilegedContainerRegex = new(
        @"(?im)^\s*privileged\s*:\s*true\s*(?:#.*)?$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex FloatingImageRegex = new(
     @"(?im)^\s*image\s*:\s*['""]?[^'""\s#]+:(?:latest|stable|main|master)['""]?\s*(?:#.*)?$",
     RegexOptions.Compiled,
     TimeSpan.FromMilliseconds(250));

    private readonly ConfigurationPolicy _policy;

    public ConfigurationEvaluator(
        ILogger<ConfigurationEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Configuration ?? new ConfigurationPolicy();
    }

    public override string Name => nameof(ConfigurationEvaluator);

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "JavaScript",
        "TypeScript"
    ];

    public override string[] SupportedFrameworks => ["*"];

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (!_policy.Enabled)
        {
            _logger.LogInformation(
                "{Evaluator} is disabled by policy.",
                Name);

            return [];
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException(
                "A project directory is required.",
                nameof(projectPath));
        }

        var fullProjectPath = Path.GetFullPath(projectPath);

        if (!Directory.Exists(fullProjectPath))
        {
            return
            [
                CreateFailureResult(
                    fullProjectPath,
                    "ProjectDirectoryNotFound",
                    $"Project directory does not exist: {fullProjectPath}")
            ];
        }

        var diagnostics = new List<string>();
        var fileResults = new List<ArchitectureEvaluatorResult>();

        foreach (var file in EnumerateConfigurationFiles(
                     fullProjectPath,
                     diagnostics,
                     token))
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException exception)
            {
                diagnostics.Add(
                    $"Could not read '{file}': {exception.Message}");

                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(
                    $"Access denied for '{file}': {exception.Message}");

                continue;
            }

            var assessment = AssessFile(file, content);

            fileResults.Add(CreateFileResult(file, assessment));
        }

        var summary = CreateSummaryResult(
            fullProjectPath,
            fileResults,
            diagnostics);

        fileResults.Add(summary);

        _logger.LogInformation(
            "{Evaluator} completed. Files analyzed: {FileCount}; " +
            "diagnostics: {DiagnosticCount}.",
            Name,
            fileResults.Count - 1,
            diagnostics.Count);

        return fileResults;
    }

    private IEnumerable<string> EnumerateConfigurationFiles(
        string projectPath,
        ICollection<string> diagnostics,
        CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(projectPath);

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var directory = pending.Pop();
            IEnumerable<string> subdirectories;
            IEnumerable<string> files;

            try
            {
                subdirectories = Directory.EnumerateDirectories(directory)
                    .ToArray();

                files = Directory.EnumerateFiles(directory)
                    .ToArray();
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(
                    $"Access denied to directory '{directory}': " +
                    exception.Message);

                continue;
            }
            catch (IOException exception)
            {
                diagnostics.Add(
                    $"Could not enumerate directory '{directory}': " +
                    exception.Message);

                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                token.ThrowIfCancellationRequested();

                var directoryName = Path.GetFileName(
                    subdirectory.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar));

                if (ExcludedDirectories.Contains(directoryName))
                {
                    continue;
                }

                pending.Push(subdirectory);
            }

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();

                if (IsConfigurationFile(file))
                {
                    yield return file;
                }
            }
        }
    }

    private FileAssessment AssessFile(string file, string content)
    {
        var assessment = new FileAssessment
        {
            Category = DetectCategory(file),
            SyntaxValidation = ValidateSyntax(file, content)
        };

        if (assessment.SyntaxValidation == SyntaxValidationStatus.Invalid)
        {
            assessment.Findings.Add(
                new Finding(
                    "CFG-SYNTAX-001",
                    "SyntaxValidation",
                    "Error",
                    "The configuration document failed syntax validation."));
        }

        if (_policy.ForbiddenKeys.Length > 0)
        {
            foreach (var key in _policy.ForbiddenKeys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (content.Contains(
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    assessment.Findings.Add(
                        new Finding(
                            "CFG-POLICY-001",
                            "ForbiddenConfigurationKey",
                            "Warning",
                            $"Configuration contains a policy-forbidden key: " +
                            $"{key}."));

                    break;
                }
            }
        }

        if (_policy.CheckInfrastructureConfigs)
        {
            AssessInfrastructureSafety(file, content, assessment);
        }

        assessment.InfrastructureSafetyScore =
            ComputeSafetyScore(assessment);

        assessment.SyntaxComplianceScore =
            assessment.SyntaxValidation switch
            {
                SyntaxValidationStatus.Valid => 100,
                SyntaxValidationStatus.Invalid => 0,
                _ => -1
            };

        assessment.ConfigurationIntegrityScore =
            ComputeIntegrityScore(assessment);

        return assessment;
    }

    private static SyntaxValidationStatus ValidateSyntax(
        string file,
        string content)
    {
        var extension = Path.GetExtension(file);

        try
        {
            if (extension.Equals(
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                using var document = JsonDocument.Parse(content);

                return SyntaxValidationStatus.Valid;
            }

            if (extension.Equals(
                    ".yaml",
                    StringComparison.OrdinalIgnoreCase)
                || extension.Equals(
                    ".yml",
                    StringComparison.OrdinalIgnoreCase))
            {
                var yaml = new YamlStream();
                yaml.Load(new StringReader(content));

                return SyntaxValidationStatus.Valid;
            }

            // No generic parser is available here for .env, Dockerfile,
            // properties, package manifests, or CI pipeline syntax.
            return SyntaxValidationStatus.NotValidated;
        }
        catch (JsonException)
        {
            return SyntaxValidationStatus.Invalid;
        }
        catch (YamlException)
        {
            return SyntaxValidationStatus.Invalid;
        }
        catch (ArgumentException)
        {
            return SyntaxValidationStatus.Invalid;
        }
    }

    private static void AssessInfrastructureSafety(
        string file,
        string content,
        FileAssessment assessment)
    {
        var fileName = Path.GetFileName(file);

        if (fileName.Equals(
                "Dockerfile",
                StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(
                "Dockerfile.",
                StringComparison.OrdinalIgnoreCase))
        {
            if (UnsafeDockerCommandRegex.IsMatch(content))
            {
                assessment.Findings.Add(
                    new Finding(
                        "CFG-DOCKER-001",
                        "UnsafeDockerCommand",
                        "Warning",
                        "A Docker instruction contains a potentially unsafe " +
                        "shell command. Review the command and its context."));
            }
        }

        if (PrivilegedContainerRegex.IsMatch(content))
        {
            assessment.Findings.Add(
                new Finding(
                    "CFG-CONTAINER-001",
                    "PrivilegedContainer",
                    "Warning",
                    "A container configuration enables privileged mode."));
        }

        if (FloatingImageRegex.IsMatch(content))
        {
            assessment.Findings.Add(
                new Finding(
                    "CFG-CONTAINER-002",
                    "FloatingContainerImageTag",
                    "Warning",
                    "A container image uses a mutable or floating tag. " +
                    "Consider an immutable digest."));
        }
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string file,
        FileAssessment assessment)
    {
        var result = new ArchitectureEvaluatorResult(Name, file)
        {
            Category = assessment.Category
        };

        result.Metadata["EvaluationScope"] = FileScope;
        result.Metadata["FilePath"] = file;
        result.Metadata["FileName"] = Path.GetFileName(file);
        result.Metadata["FileType"] = Path.GetExtension(file);
        result.Metadata["Language"] = Context?.Language ?? "Unknown";
        result.Metadata["Framework"] = DetectFramework(assessment.Category);
        result.Metadata["PolicyEnabled"] = _policy.Enabled.ToString();
        result.Metadata["SyntaxValidation"] =
            assessment.SyntaxValidation.ToString();
        result.Metrics["SyntaxComplianceScore"] =
            assessment.SyntaxComplianceScore;

        result.Metrics["InfrastructureSafetyScore"] =
            assessment.InfrastructureSafetyScore;

        result.Metrics["ConfigurationIntegrityScore"] =
            assessment.ConfigurationIntegrityScore;

        result.Metrics["FindingCount"] = assessment.Findings.Count;

        foreach (var finding in assessment.Findings)
        {
            result.Metadata[$"Finding.{finding.Id}"] =
                $"{finding.Severity}: {finding.Description}";
        }

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummaryResult(
        string projectPath,
        IReadOnlyCollection<ArchitectureEvaluatorResult> results,
        IReadOnlyCollection<string> diagnostics)
    {
        var syntaxScores = results
            .Select(r => r.Metrics.GetValueOrDefault("SyntaxComplianceScore", -1))
            .Where(score => score >= 0)
            .ToArray();

        var safetyScores = results
            .Select(r => r.Metrics.GetValueOrDefault("InfrastructureSafetyScore", -1))
            .Where(score => score >= 0)
            .ToArray();

        var integrityScores = results
            .Select(r => r.Metrics.GetValueOrDefault("ConfigurationIntegrityScore", -1))
            .Where(score => score >= 0)
            .ToArray();

        var summary = new ArchitectureEvaluatorResult(Name, projectPath)
        {
            Category = "InfrastructureSummary"
        };

        summary.Metadata["EvaluationScope"] = ProjectScope;
        summary.Metadata["ProjectPath"] = projectPath;
        summary.Metadata["PolicyEnabled"] = _policy.Enabled.ToString();
        summary.Metadata["CheckedFrameworks"] =
            string.Join(", ", SupportedFrameworks);
        summary.Metadata["AnalysisStatus"] =
            diagnostics.Count == 0 ? "Completed" : "CompletedWithDiagnostics";
        summary.Metadata["Diagnostics"] = string.Join(
            Environment.NewLine,
            diagnostics.Take(50));

        summary.Metrics["ConfigurationFileCount"] = results.Count;
        summary.Metrics["DiagnosticCount"] = diagnostics.Count;
        summary.Metrics["AverageSyntaxCompliance"] = AverageOrUnavailable(syntaxScores);
        summary.Metrics["AverageInfrastructureSafety"] = AverageOrUnavailable(safetyScores);
        summary.Metrics["AverageIntegrityScore"] = AverageOrUnavailable(integrityScores);

        var findingCount = results.Sum(
            r => r.Metrics.GetValueOrDefault("FindingCount", 0));

        summary.Metrics["FindingCount"] = findingCount;

        // -1 means unavailable; it is not interpreted as a zero score.
        summary.Metrics["InfrastructureHealthIndex"] =
            AverageOrUnavailable(integrityScores);

        return summary;
    }

    private ArchitectureEvaluatorResult CreateFailureResult(
        string projectPath,
        string status,
        string diagnostic)
    {
        var result = new ArchitectureEvaluatorResult(Name, projectPath)
        {
            Category = "InfrastructureSummary"
        };

        result.Metadata["EvaluationScope"] = ProjectScope;
        result.Metadata["ProjectPath"] = projectPath;
        result.Metadata["AnalysisStatus"] = status;
        result.Metadata["Diagnostic"] = diagnostic;

        return result;
    }

    private static double ComputeSafetyScore(FileAssessment assessment)
    {
        var safetyFindings = assessment.Findings.Count(
            finding => finding.Id.StartsWith(
                "CFG-DOCKER-",
                StringComparison.Ordinal)
                || finding.Id.StartsWith(
                    "CFG-CONTAINER-",
                    StringComparison.Ordinal));

        return safetyFindings == 0 ? 100 : 0;
    }

    private static double ComputeIntegrityScore(FileAssessment assessment)
    {
        // Unsupported formats remain unvalidated; infrastructure checks may still
        // report independently measured safety signals for those files.
        if (assessment.SyntaxComplianceScore < 0)
        {
            return -1;
        }

        var policyIntegrityScore = assessment.Findings.Any(
            finding => finding.Id == "CFG-POLICY-001")
            ? 0
            : 100;

        return Math.Round(
            assessment.SyntaxComplianceScore * 0.60
            + assessment.InfrastructureSafetyScore * 0.25
            + policyIntegrityScore * 0.15,
            2);
    }

    private static double AverageOrUnavailable(IEnumerable<double> values)
    {
        var array = values.ToArray();

        return array.Length == 0
            ? -1
            : Math.Round(array.Average(), 2);
    }

    private static bool IsConfigurationFile(string file)
    {
        var name = Path.GetFileName(file);
        var normalized = file.Replace('\\', '/');

        return Path.GetExtension(file).ToLowerInvariant() is
                   ".json" or ".yaml" or ".yml" or ".env" or ".properties"
               || name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)
               || name.Equals("package.json", StringComparison.OrdinalIgnoreCase)
               || name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("requirements-", StringComparison.OrdinalIgnoreCase)
                   && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Pipfile", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Pipfile.lock", StringComparison.OrdinalIgnoreCase)
               || name.Equals("poetry.lock", StringComparison.OrdinalIgnoreCase)
               || name.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase)
               || name.Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase)
               || name.Equals("yarn.lock", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Cargo.lock", StringComparison.OrdinalIgnoreCase)
               || name.Equals("go.mod", StringComparison.OrdinalIgnoreCase)
               || name.Equals("go.sum", StringComparison.OrdinalIgnoreCase)
               || name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)
               || name.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)
               || name.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
               || name.Equals("NuGet.config", StringComparison.OrdinalIgnoreCase)
               || name.Equals("nuget.config", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith(".gitlab-ci.yml", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase)
               || name.Equals("compose.yaml", StringComparison.OrdinalIgnoreCase)
               || name.Equals("compose.yml", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectCategory(string file)
    {
        var name = Path.GetFileName(file);
        var normalized = file.Replace('\\', '/');

        if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("compose.", StringComparison.OrdinalIgnoreCase))
        {
            return "Container";
        }

        if (normalized.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".gitlab-ci.yml", StringComparison.OrdinalIgnoreCase))
        {
            return "Pipeline";
        }

        if (name.Contains("lock", StringComparison.OrdinalIgnoreCase)
            || name.Equals("package.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("requirements-", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Pipfile", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("go.mod", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("build.gradle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NuGet.config", StringComparison.OrdinalIgnoreCase))
        {
            return "Dependencies";
        }

        if (Path.GetExtension(file).Equals(".env", StringComparison.OrdinalIgnoreCase))
        {
            return "Environment";
        }

        if (Path.GetExtension(file).Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(file).Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            return "Kubernetes";
        }

        return "Configuration";
    }

    private static string DetectFramework(string category) =>
        category switch
        {
            "Container" => "Docker",
            "Pipeline" => "CI/CD",
            "Dependencies" => "PackageManagement",
            "Kubernetes" => "Kubernetes",
            "Environment" => "Environment",
            _ => "Generic"
        };

    private enum SyntaxValidationStatus
    {
        Valid,
        Invalid,
        NotValidated
    }

    private sealed class FileAssessment
    {
        public string Category { get; set; } = "Configuration";
        public SyntaxValidationStatus SyntaxValidation { get; set; }

        public double SyntaxComplianceScore { get; set; }
        public double InfrastructureSafetyScore { get; set; }
        public double ConfigurationIntegrityScore { get; set; }

        public List<Finding> Findings { get; } = [];
    }

    private sealed record Finding(
        string Id,
        string Type,
        string Severity,
        string Description);
}
