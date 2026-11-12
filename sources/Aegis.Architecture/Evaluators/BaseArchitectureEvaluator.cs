using Aegis.Architecture.Analysis.Detectors;

using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Rules;
using Aegis.Shared.Contracts;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Utilities;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Evaluators;

/// <summary>
/// Base abstraction for Aegis evaluators.
///
/// Evaluators analyze application code and produce structured facts.
/// Rule interpretation and compliance decisions belong to RuleEngineCore.
/// </summary>
public abstract class BaseArchitectureEvaluator : IEvaluator, IScopedDependency
{
    /// <summary>
    /// Metadata key used to distinguish file-level facts from aggregate facts.
    /// </summary>
    public const string EvaluationScopeMetadataKey = "EvaluationScope";

    /// <summary>
    /// Scope identifier for facts describing an individual source file.
    /// </summary>
    public const string FileScope = "File";

    /// <summary>
    /// Scope identifier for project-wide aggregate facts.
    /// </summary>
    public const string ProjectScope = "Project";

    /// <summary>
    /// Metadata key containing the actual source file path.
    /// </summary>
    public const string FilePathMetadataKey = "FilePath";

    protected readonly ILogger _logger;

    protected ProjectArchitectureContext? Context { get; private set; }

    public abstract string Name { get; }

    public virtual string[] SupportedLanguages { get; } = ["*"];

    public virtual string[] SupportedFrameworks { get; } = ["*"];

    public virtual IReadOnlyCollection<string> RequiredSemanticCapabilities { get; } = Array.Empty<string>();

    public bool IsEnabled { get; set; } = true;

    public double WeightFactor { get; set; } = 1.0;

    protected BaseArchitectureEvaluator(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Applies evaluator-specific policy settings.
    /// Evaluators not explicitly handled here remain enabled by default.
    /// </summary>
    public virtual void ApplyPolicy(AegisArchitecturePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        switch (Name)
        {
            case "PerformanceEvaluator":
                IsEnabled = policy.Performance.Enabled;
                WeightFactor = 1.2;
                break;

            case "MaintainabilityEvaluator":
                IsEnabled =
                    policy.Maintainability.MinMaintainabilityIndex > 0;
                WeightFactor = 1.0;
                break;

            case "SecurityEvaluator":
                IsEnabled = policy.Security.Enabled;
                WeightFactor = 1.5;
                break;

            default:
                IsEnabled = true;
                WeightFactor = 1.0;
                break;
        }

        _logger.LogDebug(
            "Evaluator {Name}: Enabled={Enabled}, Weight={Weight}",
            Name,
            IsEnabled,
            WeightFactor);
    }

    /// <summary>
    /// Executes this evaluator and normalizes the metadata of its results.
    /// Results are materialized once so deferred enumerables are not enumerated
    /// repeatedly and returned results are stable.
    /// </summary>
    public async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateAsync(
        string projectPath,
        ProjectArchitectureContext context,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(context);

        token.ThrowIfCancellationRequested();

        Context = context;

        if (!IsEnabled)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Info,
                $"Evaluator disabled by policy: {Name}");

            return
            [
                CreateExecutionStatusFact(
                    projectPath,
                    "DisabledByPolicy",
                    $"Evaluator '{Name}' is disabled by the active policy.")
            ];
        }

        try
        {
            var evaluated = await EvaluateCoreAsync(projectPath, token);

            if (evaluated is null)
            {
                throw new InvalidOperationException(
                    $"Evaluator '{Name}' returned a null result sequence.");
            }

            var results = evaluated.ToList();

            foreach (var result in results)
            {
                token.ThrowIfCancellationRequested();

                if (result is null)
                {
                    throw new InvalidOperationException(
                        $"Evaluator '{Name}' returned a null result.");
                }

                result.Source = Name;
                result.WeightFactor = WeightFactor;
                result.WasEnabled = true;
                result.ProjectName ??= context.ProjectName;
                result.Language ??= context.Language;

                // Framework evidence is attached to each file fact independently.
                // Do not copy a mixed repository's primary framework onto every
                // file: Razor, Unity and backend files can coexist in one scan.
                var evidencePath = result.Metadata?.GetValueOrDefault(FilePathMetadataKey)
                    ?? result.File;
                if (!string.IsNullOrWhiteSpace(evidencePath))
                {
                    if (result.Metadata is { } metadata
                        && metadata.ContainsKey(FilePathMetadataKey))
                    {
                        metadata["SourceOwnership"] =
                            FileExclusions.ClassifySourceOwnership(evidencePath);
                    }

                    var framework = ResolveFrameworkForFile(
                        evidencePath,
                        projectPath,
                        context);
                    if (!string.IsNullOrWhiteSpace(framework))
                    {
                        result.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        result.Metadata["Framework"] = framework;
                        result.Framework = framework;
                    }
                }

                // Infer file scope only when a real file-path metadata entry
                // exists. Do not infer scope from Target: it may identify a
                // project, module, component, or other aggregate.
                if (result.Metadata is not null
                    && result.Metadata.ContainsKey(FilePathMetadataKey)
                    && !result.Metadata.ContainsKey(EvaluationScopeMetadataKey))
                {
                    result.Metadata[EvaluationScopeMetadataKey] = FileScope;
                }
            }

            token.ThrowIfCancellationRequested();

            return results;
        }
        catch (OperationCanceledException)
        {
            // Cancellation must propagate in both Debug and Release builds.
            throw;
        }
        catch (Exception ex)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Error,
                $"Evaluator failed: {Name}",
                ex);

            _logger.LogError(
                ex,
                "Evaluator {EvaluatorName} failed for project {ProjectPath}.",
                Name,
                projectPath);

#if DEBUG
            throw;
#else
            return [];
#endif
        }
    }

    protected static string ResolveLanguageForFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            ".ts" or ".tsx" => "TypeScript",
            ".js" or ".jsx" => "JavaScript",
            _ => "Unknown"
        };
    }

    protected string? ResolveLayerForFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return Context?.Layers.FirstOrDefault(layer =>
            layer.Files.Contains(filePath, StringComparer.OrdinalIgnoreCase))?.Name;
    }

    protected static string? ResolveFrameworkForFile(
        string filePath,
        string projectPath,
        ProjectArchitectureContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(context);

        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".razor", StringComparison.OrdinalIgnoreCase)
            || filePath.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "Blazor";
        }

        if (extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase))
            return "Razor";

        if (IsWithinUnityProject(filePath, projectPath))
            return "Unity";

        var owningModule = context.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .OrderByDescending(module => module.Path.Length)
            .FirstOrDefault(module => IsSameOrDescendantPath(filePath, module.Path));

        var moduleFrameworks = owningModule?.Frameworks
            ?? [];

        if (extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase))
        {
            return FindFramework(moduleFrameworks, "React")
                ?? (owningModule is null && context.Modules.Count == 0
                    ? FindFramework(context.DetectedFrameworks, "React")
                    : null);
        }

        if (extension.Equals(".ts", StringComparison.OrdinalIgnoreCase))
        {
            var moduleClientFrameworks = moduleFrameworks
                .Where(framework => framework is "Angular" or "React")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (moduleClientFrameworks.Length == 1)
                return moduleClientFrameworks[0];

            if (moduleClientFrameworks.Length > 1)
                return null;

            if (owningModule is not null || context.Modules.Count > 0)
                return null;

            var repositoryClientFrameworks = context.DetectedFrameworks
                .Where(framework =>
                    string.Equals(framework, "Angular", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(framework, "React", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return repositoryClientFrameworks.Length == 1
                ? repositoryClientFrameworks[0]
                : null;
        }

        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            return FindFramework(moduleFrameworks, "Angular")
                ?? (owningModule is null && context.Modules.Count == 0
                    ? FindFramework(context.DetectedFrameworks, "Angular")
                    : null);
        }

        if (owningModule is not null)
        {
            if (!string.Equals(
                    owningModule.Framework,
                    "Multi-Framework",
                    StringComparison.OrdinalIgnoreCase))
            {
                return owningModule.Framework;
            }

            // Multi-Framework is an aggregate label, not a concrete technology.
            // For C# files, resolve the owning project's server/runtime framework;
            // Razor/Blazor and frontend files were resolved by extension above.
            if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return moduleFrameworks.FirstOrDefault(framework =>
                    framework is "ASP.NET Core"
                        or ".NET (EF Core)"
                        or ".NET"
                        or "WPF"
                        or "Windows Forms");
            }

            return moduleFrameworks.Count == 1
                ? moduleFrameworks[0]
                : null;
        }

        // The repository context is only a fallback when no project owns the
        // file. A mixed repository must not assign its aggregate framework to
        // every otherwise-unclassified source file.
        return context.Modules.Count == 0
            ? context.Framework
            : null;
    }

    private static string? FindFramework(
        IEnumerable<string> frameworks,
        string expected) =>
        frameworks.FirstOrDefault(framework =>
            string.Equals(
                framework,
                expected,
                StringComparison.OrdinalIgnoreCase));

    private static bool IsWithinUnityProject(string filePath, string projectPath)
    {
        try
        {
            var boundary = Path.GetFullPath(projectPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));

            while (!string.IsNullOrWhiteSpace(directory))
            {
                if (Path.GetFileName(directory).Equals(
                        "Assets",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var unityRoot = Directory.GetParent(directory)?.FullName;
                    if (unityRoot is not null
                        && Directory.Exists(Path.Combine(unityRoot, "Assets"))
                        && (Directory.Exists(Path.Combine(unityRoot, "ProjectSettings"))
                            || Directory.Exists(Path.Combine(unityRoot, "Packages"))))
                    {
                        return true;
                    }
                }

                var normalizedDirectory = directory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                if (string.Equals(normalizedDirectory, boundary, StringComparison.OrdinalIgnoreCase))
                    break;

                var parent = Directory.GetParent(directory)?.FullName;
                if (parent is null
                    || !parent.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                directory = parent;
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            // Framework attribution is best-effort; failed probing is not a scan failure.
        }

        return false;
    }

    /// <summary>
    /// Performs the evaluator-specific analysis.
    /// Implementations should observe the cancellation token during long scans.
    /// </summary>
    protected abstract Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token);

    /// <summary>
    /// Enumerates source files matching the requested extensions while
    /// restricting analysis to the application's backend/frontend boundaries.
    /// </summary>
    protected IReadOnlyList<string> ResolveSourceFiles(
        string projectPath,
        params string[] extensions) =>
        ResolveSourceFiles(
            projectPath,
            includeTestProjects: false,
            extensions: extensions);

    protected IReadOnlyList<string> ResolveSourceFiles(
        string projectPath,
        bool includeTestProjects,
        params string[] extensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(extensions);

        if (extensions.Length == 0)
            return [];

        return EnumerateGovernedFiles(projectPath)
            .Where(file => extensions.Any(extension =>
                !string.IsNullOrWhiteSpace(extension)
                && file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            .Where(file =>
                IsApplicationCode(file, projectPath)
                || IsWithinDetectedModule(file))
            .Where(file => includeTestProjects || !IsTestProjectFile(file))
            .ToList();
    }

    protected bool IsTestProjectFile(string file) =>
        Context?.Modules.Any(module =>
            (module.IsTestProject
                || string.Equals(
                    module.ProjectKind,
                    "Test",
                    StringComparison.OrdinalIgnoreCase))
            && IsSameOrDescendantPath(file, module.Path)) == true;

    private bool IsWithinDetectedModule(string file) =>
        Context?.Modules.Any(module =>
            IsSameOrDescendantPath(file, module.Path)) == true;

    private static List<string> EnumerateGovernedFiles(string rootPath)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(rootPath));

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (FileExclusions.IsExcluded(directory) || IsExcludedDir(directory))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    if (!FileExclusions.IsExcluded(file)
                        && !IsExcludedDir(file)
                        && IsGovernanceSource(file))
                    {
                        files.Add(file);
                    }
                }

                foreach (var child in Directory.EnumerateDirectories(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    if (FileExclusions.IsExcluded(child) || IsExcludedDir(child))
                        continue;

                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    pending.Push(child);
                }
            }
            catch (IOException)
            {
                // Preserve evidence from readable siblings when a subtree is unavailable.
            }
            catch (UnauthorizedAccessException)
            {
                // An unreadable subtree is not treated as compliant evidence.
            }
        }

        return files;
    }

    private static bool IsUnityTempPath(string path)
    {
        var current = Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path));

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Path.GetFileName(current).Equals(
                    "Temp",
                    StringComparison.OrdinalIgnoreCase))
            {
                var root = Directory.GetParent(current)?.FullName;
                return root is not null
                    && Directory.Exists(Path.Combine(root, "Assets"))
                    && (Directory.Exists(Path.Combine(root, "ProjectSettings"))
                        || Directory.Exists(Path.Combine(root, "Packages")));
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return false;
    }

    private static bool IsTextMeshProExamplePath(string path)
    {
        var segments = path.Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var hasTextMeshProDirectory = segments.Any(segment =>
            segment.Equals("TextMesh Pro", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("TextMeshPro", StringComparison.OrdinalIgnoreCase));
        var hasExamplesDirectory = segments.Any(segment =>
            segment.Equals("Examples & Extras", StringComparison.OrdinalIgnoreCase));

        return hasTextMeshProDirectory && hasExamplesDirectory;
    }

    private static bool IsGovernanceSource(string file)
    {
        var normalized = file.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);

        var segments = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        // Treat vendored libraries and tutorial/sample trees as external code,
        // not as first-party architectural obligations. A future policy option
        // can opt these paths back in when a team intentionally governs them.
        if (IsTextMeshProExamplePath(file)
            || IsUnityTempPath(file)
            || segments.Any(segment =>
                segment.Equals("ThirdParty", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("Third-Party", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("vendor", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("Generated", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("PackageCache", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("Logs", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("TestResults", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("coverage", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("dist", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("build", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var name = Path.GetFileName(file);
        return !name.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApplicationCode(string file, string projectPath)
    {
        // A discovered project is its own source boundary. Its files do not
        // need to repeat repository-level directory names such as "back",
        // "front", or "Assets" to be eligible for analysis.
        if (Directory.EnumerateFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).Any()
            || File.Exists(Path.Combine(projectPath, "package.json"))
            || File.Exists(Path.Combine(projectPath, "pom.xml"))
            || Directory.Exists(Path.Combine(projectPath, "Assets")))
        {
            return true;
        }

        return IsApplicationCode(file);
    }

    /// <summary>
    /// Returns true when a file is within the supported backend/frontend
    /// application boundaries and outside known generated or external folders.
    /// </summary>
    private static bool IsApplicationCode(string file)
    {
        var normalized = file.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);

        var ignoredSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}Library{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}Logs{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"
        };

        if (ignoredSegments.Any(segment =>
                normalized.Contains(
                    segment,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var pathParts = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        // Preserve conventional backend/frontend roots and Unity source trees.
        // Unity projects are often checked in under a folder named "client";
        // that folder name alone is not evidence that source should be ignored.
        return pathParts.Any(part =>
            part.Equals("back", StringComparison.OrdinalIgnoreCase)
            || part.Equals("front", StringComparison.OrdinalIgnoreCase)
            || part.Equals("Assets", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Delegates generated, temporary, and other excluded-path checks to the
    /// shared Aegis path-filtering utility.
    /// </summary>
    protected static bool IsExcludedDir(string path) =>
        PathUtils.IsExcludedDir(path);

    /// <summary>
    /// Creates a file-level evaluator fact using the filename as Target and
    /// the original path in metadata for layer resolution and diagnostics.
    /// </summary>
    /// <summary>
    /// Creates an explicit project-scope execution status when an evaluator
    /// cannot analyze a scope. Status facts explain missing evidence; they are
    /// never measurements and must not satisfy rule metrics.
    /// </summary>
    protected ArchitectureEvaluatorResult CreateExecutionStatusFact(
        string projectPath,
        string status,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ArchitectureEvaluatorResult(
            Name,
            Path.GetFullPath(projectPath))
        {
            ProjectName = Context?.ProjectName,
            Language = Context?.Language,
            Framework = Context?.Framework,
            WeightFactor = WeightFactor,
            WasEnabled = IsEnabled,
            Category = "EvaluatorStatus",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["EvaluatorExecutionStatus"] = status,
                ["AnalysisStatus"] = status,
                ["StatusReason"] = reason,
                ["ProjectPath"] = Path.GetFullPath(projectPath)
            }
        };
    }

    protected ArchitectureEvaluatorResult CreateResult(
        string file,
        string? category = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        var result = new ArchitectureEvaluatorResult(
            Name,
            Path.GetFileName(file))
        {
            Category = category,
            File = file,

            Metrics = new Dictionary<string, double>(),

            Metadata = new Dictionary<string, string>
            {
                [FilePathMetadataKey] = file,
                [EvaluationScopeMetadataKey] = FileScope
            }
        };

        var owningModule = Context?.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .OrderByDescending(module => module.Path.Length)
            .FirstOrDefault(module => IsSameOrDescendantPath(file, module.Path));

        if (owningModule is not null)
        {
            result.ProjectName = owningModule.Name;
            result.Metadata["ProjectName"] = owningModule.Name;
            result.Metadata["ProjectKind"] = owningModule.ProjectKind;
            result.Metadata["IsTestProject"] = owningModule.IsTestProject.ToString();
            result.Metadata["ProjectFile"] = owningModule.ProjectFile ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(owningModule.Framework))
                result.Metadata["Framework"] = owningModule.Framework;
        }

        return result;
    }

    protected static bool IsSameOrDescendantPath(
        string candidate,
        string parent)
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

    /// <summary>
    /// Converts evaluator facts to legacy rule results.
    /// Compliance rule interpretation should normally be performed by
    /// RuleEngineCore instead.
    /// </summary>
    protected static IEnumerable<ArchitectureRuleresult> ConvertToRuleResults(
        IEnumerable<ArchitectureEvaluatorResult> evalResults,
        string ruleId)
    {
        ArgumentNullException.ThrowIfNull(evalResults);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);

        return evalResults.Select(result =>
        {
            var category = ArchitectureRuleCategory.General;

            if (!string.IsNullOrWhiteSpace(result.Category)
                && Enum.TryParse(
                    result.Category,
                    true,
                    out ArchitectureRuleCategory parsed))
            {
                category = parsed;
            }

            return new ArchitectureRuleresult
            {
                RuleId = ruleId,

                Message =
                    $"{result.Source}: {Path.GetFileName(result.Target)} " +
                    $"({string.Join(", ", result.Metrics.Select(metric =>
                        $"{metric.Key}={metric.Value:0.##}"))})",

                Category = category,
                Severity = ArchitectureRuleSeverity.Info,
                Target = result.Target
            };
        });
    }

    /// <summary>
    /// Enumerates files under backend/frontend application boundaries.
    /// This method intentionally preserves its existing behavior: callers
    /// needing extension or excluded-directory filtering should use
    /// ResolveSourceFiles instead.
    /// </summary>
    protected static IEnumerable<string> EnumerateApplicationFiles(
        string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        return Directory
            .EnumerateFiles(
                projectPath,
                "*.*",
                SearchOption.AllDirectories)
            .Where(file =>
            {
                var relative = Path.GetRelativePath(projectPath, file);

                return relative.StartsWith(
                           $"back{Path.DirectorySeparatorChar}",
                           StringComparison.OrdinalIgnoreCase)
                       || relative.StartsWith(
                           $"front{Path.DirectorySeparatorChar}",
                           StringComparison.OrdinalIgnoreCase);
            });
    }
}
