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
/// structural organization, dependency hygiene, and maintainability.
/// Produces RepositoryHealthIndex (0-100).
/// </summary>
public sealed class RepositoryEvaluator : BaseArchitectureEvaluator
{
    private readonly RepositoryPolicy _policy;

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


    private static readonly Regex TodoRx =
        new(@"\b(TODO|FIXME|HACK|XXX)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


    private static readonly string[] DependencyLockFiles =
    [
        "package-lock.json",
        "yarn.lock",
        "pnpm-lock.yaml",
        "poetry.lock",
        "Pipfile.lock",
        "packages.lock.json",
        "pom.xml"
    ];


    public RepositoryEvaluator(
        ILogger<RepositoryEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy = options.Value.Repository ?? new RepositoryPolicy();
    }


    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        _logger.LogInformation(
            "🏗️ Running {Evaluator} on {Path}",
            Name,
            projectPath);


        var allFiles = Directory
            .EnumerateFiles(
                projectPath,
                "*.*",
                SearchOption.AllDirectories)
            .Where(f => !IsExcludedDir(f))
            .ToList();


        double governanceScore = 1;
        double cicdScore = 1;
        double structureScore = 1;
        double dependencyScore = 1;
        double maintainabilityScore = 1;


        int missingFiles = 0;
        int todoCount = 0;
        int projectCount = 0;
        int largeFiles = 0;


        // ==========================================================
        // Governance
        // ==========================================================

        if (_policy.EnforceGovernanceFiles)
        {
            foreach (var required in _policy.RequiredFiles)
            {
                if (!File.Exists(Path.Combine(projectPath, required)))
                {
                    missingFiles++;
                    governanceScore -= 0.1;
                }
            }


            if (_policy.RequireContributingGuide &&
                !File.Exists(Path.Combine(projectPath, "CONTRIBUTING.md")))
            {
                governanceScore -= 0.05;
            }


            if (_policy.RequireVersionFile &&
                !File.Exists(Path.Combine(projectPath, "CHANGELOG.md")) &&
                !File.Exists(Path.Combine(projectPath, "version.json")))
            {
                governanceScore -= 0.05;
            }
        }



        // ==========================================================
        // CI/CD maturity
        // ==========================================================

        if (_policy.EnforceCiPresence)
        {
            bool hasPipeline = _policy.CiPaths.Any(path =>
                Directory.Exists(Path.Combine(projectPath, path)) ||
                File.Exists(Path.Combine(projectPath, path)));


            if (!hasPipeline)
                cicdScore -= 0.4;
        }



        // ==========================================================
        // Repository structure
        // ==========================================================

        if (_policy.CheckLargeFiles)
        {
            largeFiles = allFiles.Count(file =>
            {
                try
                {
                    return new FileInfo(file).Length >
                           _policy.MaxFileSizeBytes;
                }
                catch
                {
                    return false;
                }
            });


            if (largeFiles > 0)
            {
                structureScore -=
                    Math.Min(
                        0.3,
                        largeFiles * 0.02);
            }
        }



        // ==========================================================
        // Technical debt density
        // ==========================================================

        if (_policy.CheckTodoDensity)
        {
            var sourceFiles = allFiles
                .Where(IsSourceFile)
                .ToList();


            foreach (var file in sourceFiles)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var content =
                        await File.ReadAllTextAsync(file, token);

                    todoCount +=
                        TodoRx.Matches(content).Count;
                }
                catch
                {
                }
            }


            if (sourceFiles.Count > 0)
            {
                var density =
                    todoCount /
                    (double)sourceFiles.Count;


                if (density >= _policy.MaxTodoDensity)
                    maintainabilityScore -= 0.15;
            }
        }



        // ==========================================================
        // Repository depth
        // ==========================================================

        var maxDepth =
            Directory
                .EnumerateDirectories(
                    projectPath,
                    "*",
                    SearchOption.AllDirectories)
                .Select(dir =>
                    dir.Replace(projectPath, "")
                       .Count(c =>
                           c == Path.DirectorySeparatorChar))
                .DefaultIfEmpty(0)
                .Max();


        if (maxDepth > _policy.MaxNestedDepth)
            structureScore -= 0.1;



        if (allFiles.Count > _policy.MaxFileCount)
            structureScore -= 0.1;



        // ==========================================================
        // Dependency governance
        // ==========================================================

        if (_policy.RequireDependencyLock)
        {
            bool hasLock =
                allFiles.Any(file =>
                    DependencyLockFiles.Any(lockFile =>
                        file.EndsWith(
                            lockFile,
                            StringComparison.OrdinalIgnoreCase)));


            if (!hasLock)
                dependencyScore -= 0.2;
        }



        // ==========================================================
        // Multi project repository density
        // ==========================================================

        projectCount =
            allFiles.Count(file =>
                file.EndsWith(".csproj") ||
                file.EndsWith("package.json") ||
                file.EndsWith("pom.xml"));


        if (projectCount > _policy.MaxProjectsPerRepo)
            structureScore -= 0.15;



        // ==========================================================
        // Final computation
        // ==========================================================

        var health =
            ComputeRepositoryHealth(
                governanceScore,
                cicdScore,
                structureScore,
                dependencyScore,
                maintainabilityScore);



        results.Add(
            new ArchitectureEvaluatorResult(
                Name,
                projectPath)
            {
                Category = "Repository",

                Metrics = new Dictionary<string, double>
                {
                    ["GovernanceScore"] =
                        Clamp(governanceScore),

                    ["CICDScore"] =
                        Clamp(cicdScore),

                    ["StructureScore"] =
                        Clamp(structureScore),

                    ["DependencyScore"] =
                        Clamp(dependencyScore),

                    ["MaintainabilityScore"] =
                        Clamp(maintainabilityScore),

                    ["MissingGovernanceFiles"] =
                        missingFiles,

                    ["LargeFileCount"] =
                        largeFiles,

                    ["TodoCount"] =
                        todoCount,

                    ["ProjectCount"] =
                        projectCount,

                    ["RepositoryHealthIndex"] =
                        health
                },


                Metadata = new Dictionary<string, string>
                {
                    ["Evaluator"] = Name,
                    ["PolicyEnabled"] =
                        _policy.EnforceGovernanceFiles.ToString(),

                    ["ProjectPath"] =
                        projectPath
                }
            });



        _logger.LogInformation(
            "✅ {Evaluator} completed RepositoryHealthIndex={Health}",
            Name,
            health);


        return results;
    }



    private static double ComputeRepositoryHealth(
        double governance,
        double cicd,
        double structure,
        double dependency,
        double maintainability)
    {
        var score =
            governance * 0.25 +
            cicd * 0.20 +
            structure * 0.25 +
            dependency * 0.15 +
            maintainability * 0.15;


        return Math.Round(
            Clamp(score) * 100,
            2);
    }



    private static double Clamp(double value)
        => Math.Max(0, Math.Min(1, value));



    private static bool IsSourceFile(string file)
    {
        return
            file.EndsWith(".cs") ||
            file.EndsWith(".java") ||
            file.EndsWith(".ts") ||
            file.EndsWith(".js") ||
            file.EndsWith(".py") ||
            file.EndsWith(".cpp") ||
            file.EndsWith(".h");
    }
}