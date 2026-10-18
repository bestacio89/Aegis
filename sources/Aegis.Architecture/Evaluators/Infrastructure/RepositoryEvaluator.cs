
using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Infrastructure;

/// <summary>
/// Evaluates repository governance, documentation, CI/CD readiness,
/// structural organization, dependency lockfile presence, and maintainability.
///
/// Produces repository-level metrics:
/// - GovernanceScore
/// - CICDScore
/// - StructureScore
/// - DependencyScore
/// - MaintainabilityScore
/// - RepositoryHealthIndex
///
/// Component scores use a 0-1 scale. RepositoryHealthIndex uses a 0-100 scale.
/// </summary>
public sealed class RepositoryEvaluator : BaseArchitectureEvaluator
{
    private readonly RepositoryPolicy _policy;

    private static readonly Regex TodoRx = new(
        @"\b(TODO|FIXME|HACK|XXX)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            ".idea",
            "bin",
            "obj",
            "node_modules",
            "dist",
            "build",
            "coverage",
            "vendor",
            "__pycache__",
            ".venv",
            "venv",
            "TestResults"
        };

    private static readonly HashSet<string> DependencyLockFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "package-lock.json",
            "npm-shrinkwrap.json",
            "yarn.lock",
            "pnpm-lock.yaml",
            "bun.lock",
            "bun.lockb",
            "poetry.lock",
            "Pipfile.lock",
            "uv.lock",
            "packages.lock.json",
            "Cargo.lock",
            "Gemfile.lock",
            "composer.lock"
        };

    public override string Name => "RepositoryEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "JavaScript",
        "TypeScript",
        "Python",
        "Java"
    ];

    public override string[] SupportedFrameworks =>
    [
        ".NET",
        "Node",
        "Python",
        "Java",
        "Platform"
    ];

    public RepositoryEvaluator(
        ILogger<RepositoryEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Repository ?? new RepositoryPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        token.ThrowIfCancellationRequested();

        if (!Directory.Exists(projectPath))
        {
            _logger.LogWarning(
                "{Evaluator} skipped because repository directory does not exist: {Path}",
                Name,
                projectPath);

            return [];
        }

        if (!_policy.EnforceGovernanceFiles &&
            !_policy.EnforceCiPresence &&
            !_policy.CheckLargeFiles &&
            !_policy.CheckTodoDensity &&
            !_policy.RequireDependencyLock)
        {
            _logger.LogInformation(
                "{Evaluator} disabled by policy.",
                Name);

            return [];
        }

        var rootPath = Path.GetFullPath(projectPath);

        _logger.LogInformation(
            "Running {Evaluator} on repository {Path}.",
            Name,
            rootPath);

        var files = EnumerateRepositoryFiles(rootPath, token);

        token.ThrowIfCancellationRequested();

        var governance = EvaluateGovernance(rootPath);
        var cicd = EvaluateCiCd(rootPath);

        var structure = _policy.CheckLargeFiles
            ? EvaluateStructure(files)
            : RepositoryMetric.Success();

        var maintainability = _policy.CheckTodoDensity
            ? await EvaluateMaintainabilityAsync(files, token)
            : RepositoryMetric.Success();

        var dependency = EvaluateDependencies(files);

        token.ThrowIfCancellationRequested();

        var health = ComputeRepositoryHealth(
            governance.Score,
            cicd.Score,
            structure.Score,
            dependency.Score,
            maintainability.Score);

        var result = new ArchitectureEvaluatorResult(Name, rootPath)
        {
            Category = "Repository",

            Metrics =
            {
                ["GovernanceScore"] = governance.Score,
                ["CICDScore"] = cicd.Score,
                ["StructureScore"] = structure.Score,
                ["DependencyScore"] = dependency.Score,
                ["MaintainabilityScore"] = maintainability.Score,

                ["MissingGovernanceFiles"] = governance.MissingFiles,
                ["LargeFileCount"] = structure.LargeFiles,
                ["TodoCount"] = maintainability.TodoCount,
                ["ProjectCount"] = structure.ProjectCount,

                ["RepositoryHealthIndex"] = health
            },

            Metadata =
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["Evaluator"] = Name,
                ["ProjectPath"] = rootPath,
                ["PolicyEnabled"] = "True",
                ["RepositoryFileCount"] = files.Count.ToString(),
                ["GovernanceStatus"] = governance.Status,
                ["CICDStatus"] = cicd.Status,
                ["StructureStatus"] = structure.Status,
                ["MaintainabilityStatus"] = maintainability.Status,
                ["DependencyStatus"] = dependency.Status
            }
        };

        _logger.LogInformation(
            "{Evaluator} completed RepositoryHealthIndex={Health}.",
            Name,
            health);

        return [result];
    }

    private RepositoryMetric EvaluateGovernance(string projectPath)
    {
        if (!_policy.EnforceGovernanceFiles)
        {
            return RepositoryMetric.Success();
        }

        var requiredFiles = new HashSet<string>(
            _policy.RequiredFiles ?? [],
            StringComparer.OrdinalIgnoreCase);

        if (_policy.RequireContributingGuide)
        {
            requiredFiles.Add("CONTRIBUTING.md");
        }

        // The version requirement is satisfied by either CHANGELOG.md
        // or version.json. Do not independently require both files.
        if (_policy.RequireVersionFile)
        {
            requiredFiles.Remove("CHANGELOG.md");
            requiredFiles.Remove("version.json");
        }

        var missingFiles = new List<string>();
        var totalRequirements = requiredFiles.Count;
        var satisfiedRequirements = 0;

        foreach (var requiredFile in requiredFiles)
        {
            var fullPath = ResolveRepositoryPath(
                projectPath,
                requiredFile);

            if (File.Exists(fullPath))
            {
                satisfiedRequirements++;
            }
            else
            {
                missingFiles.Add(requiredFile);
            }
        }

        if (_policy.RequireVersionFile)
        {
            totalRequirements++;

            var hasVersionFile =
                File.Exists(Path.Combine(projectPath, "CHANGELOG.md")) ||
                File.Exists(Path.Combine(projectPath, "version.json"));

            if (hasVersionFile)
            {
                satisfiedRequirements++;
            }
            else
            {
                missingFiles.Add("CHANGELOG.md or version.json");
            }
        }

        var violations = missingFiles.Count;

        // Preserve the original scoring model while accounting for every
        // missing governance requirement.
        var score = Clamp(1 - violations * 0.1);

        return new RepositoryMetric
        {
            Score = score,
            MissingFiles = violations,
            Status = violations == 0
                ? "Compliant"
                : "RequirementsMissing",
            Details = string.Join(", ", missingFiles)
        };
    }

    private RepositoryMetric EvaluateCiCd(string projectPath)
    {
        if (!_policy.EnforceCiPresence)
        {
            return RepositoryMetric.Success();
        }

        var configuredPaths = _policy.CiPaths ?? [];

        var exists = configuredPaths.Any(path =>
        {
            var resolvedPath = ResolveRepositoryPath(projectPath, path);

            return File.Exists(resolvedPath) ||
                   Directory.Exists(resolvedPath);
        });

        return new RepositoryMetric
        {
            Score = exists ? 1 : 0.6,
            Status = exists ? "ConfigurationFound" : "ConfigurationMissing"
        };
    }

    private RepositoryMetric EvaluateStructure(
        IReadOnlyCollection<string> files)
    {
        var largeFiles = 0;

        foreach (var file in files)
        {
            try
            {
                if (new FileInfo(file).Length > _policy.MaxFileSizeBytes)
                {
                    largeFiles++;
                }
            }
            catch (IOException exception)
            {
                _logger.LogDebug(
                    exception,
                    "Unable to inspect file size for {File}.",
                    file);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogDebug(
                    exception,
                    "Access denied while inspecting file size for {File}.",
                    file);
            }
        }

        var projectCount = files.Count(IsProjectFile);

        var score = 1d;

        if (largeFiles > 0)
        {
            score -= Math.Min(0.3, largeFiles * 0.02);
        }

        if (projectCount > _policy.MaxProjectsPerRepo)
        {
            score -= 0.15;
        }

        return new RepositoryMetric
        {
            Score = Clamp(score),
            LargeFiles = largeFiles,
            ProjectCount = projectCount,
            Status = "Evaluated"
        };
    }

    private async Task<RepositoryMetric> EvaluateMaintainabilityAsync(
        IReadOnlyCollection<string> files,
        CancellationToken token)
    {
        if (!_policy.CheckTodoDensity)
        {
            return RepositoryMetric.Success();
        }

        var sourceFiles = files
            .Where(IsSourceFile)
            .ToArray();

        var todoCount = 0;
        var unreadableFiles = 0;

        foreach (var file in sourceFiles)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var content = await File.ReadAllTextAsync(file, token);

                todoCount += TodoRx.Matches(content).Count;
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException exception)
            {
                unreadableFiles++;

                _logger.LogDebug(
                    exception,
                    "Unable to inspect maintainability indicators in {File}.",
                    file);
            }
            catch (UnauthorizedAccessException exception)
            {
                unreadableFiles++;

                _logger.LogDebug(
                    exception,
                    "Access denied while inspecting {File}.",
                    file);
            }
        }

        var score = 1d;

        if (sourceFiles.Length > 0)
        {
            var density = todoCount / (double)sourceFiles.Length;

            if (density >= _policy.MaxTodoDensity)
            {
                score -= 0.15;
            }
        }

        return new RepositoryMetric
        {
            Score = Clamp(score),
            TodoCount = todoCount,
            Status = unreadableFiles > 0
                ? "PartiallyEvaluated"
                : "Evaluated",
            UnreadableFiles = unreadableFiles
        };
    }

    private RepositoryMetric EvaluateDependencies(
        IReadOnlyCollection<string> files)
    {
        if (!_policy.RequireDependencyLock)
        {
            return RepositoryMetric.Success();
        }

        // Compare actual filenames rather than suffixes. In particular,
        // pom.xml is a Maven project descriptor, not a dependency lockfile.
        var hasLockFile = files.Any(file =>
            DependencyLockFiles.Contains(Path.GetFileName(file)));

        return new RepositoryMetric
        {
            Score = hasLockFile ? 1 : 0.8,
            Status = hasLockFile
                ? "LockfileFound"
                : "LockfileMissing"
        };
    }

    private static List<string> EnumerateRepositoryFiles(
        string rootPath,
        CancellationToken token)
    {
        var files = new List<string>();
        var pendingDirectories = new Stack<string>();

        pendingDirectories.Push(rootPath);

        while (pendingDirectories.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var currentDirectory = pendingDirectories.Pop();

            IEnumerable<string> childDirectories;

            try
            {
                childDirectories = Directory
                    .EnumerateDirectories(currentDirectory)
                    .ToArray();
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

                var directoryName = Path.GetFileName(
                    Path.TrimEndingDirectorySeparator(directory));

                if (ExcludedDirectories.Contains(directoryName))
                {
                    continue;
                }

                try
                {
                    // Avoid traversing directory links that could lead outside
                    // the repository or create recursive traversal.
                    if ((File.GetAttributes(directory) &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                pendingDirectories.Push(directory);
            }

            try
            {
                // "*" includes extensionless files such as Dockerfile and
                // does not omit files without an extension.
                files.AddRange(Directory.EnumerateFiles(currentDirectory, "*"));
            }
            catch (UnauthorizedAccessException)
            {
                // Continue scanning the rest of the repository.
            }
            catch (IOException)
            {
                // Continue scanning the rest of the repository.
            }
        }

        return files;
    }

    private static string ResolveRepositoryPath(
        string projectPath,
        string relativePath)
    {
        var root = Path.GetFullPath(projectPath);
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        var relative = Path.GetRelativePath(root, resolved);

        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            relative.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Repository policy path '{relativePath}' escapes the repository root.");
        }

        return resolved;
    }

    private static bool IsProjectFile(string file)
    {
        var name = Path.GetFileName(file);
        var extension = Path.GetExtension(file);

        return
            extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("package.json", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("build.gradle", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSourceFile(string file)
    {
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => true,
            ".java" => true,
            ".ts" => true,
            ".tsx" => true,
            ".js" => true,
            ".jsx" => true,
            ".py" => true,
            ".cpp" => true,
            ".c" => true,
            ".h" => true,
            ".hpp" => true,
            ".go" => true,
            ".rs" => true,
            _ => false
        };
    }

    private static double ComputeRepositoryHealth(
        double governance,
        double cicd,
        double structure,
        double dependency,
        double maintainability)
    {
        return Math.Round(
            Clamp(
                governance * 0.25 +
                cicd * 0.20 +
                structure * 0.25 +
                dependency * 0.15 +
                maintainability * 0.15) * 100,
            2);
    }

    private static double Clamp(double value) =>
        Math.Max(0, Math.Min(1, value));

    private sealed class RepositoryMetric
    {
        public double Score { get; init; }

        public int MissingFiles { get; init; }

        public int LargeFiles { get; init; }

        public int TodoCount { get; init; }

        public int ProjectCount { get; init; }

        public int UnreadableFiles { get; init; }

        public string Status { get; init; } = "Evaluated";

        public string Details { get; init; } = string.Empty;

        public static RepositoryMetric Success() =>
            new()
            {
                Score = 1,
                Status = "NotRequired"
            };
    }
}
