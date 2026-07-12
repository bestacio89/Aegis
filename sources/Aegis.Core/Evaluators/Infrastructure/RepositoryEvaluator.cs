using Aegis.Architecture.Evaluators;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Infrastructure;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.Infrastructure;

/// <summary>
/// Evaluates repository governance, documentation, CI/CD readiness,
/// structural organization, dependency hygiene, and maintainability.
///
/// Produces:
/// - GovernanceScore
/// - CICDScore
/// - StructureScore
/// - DependencyScore
/// - MaintainabilityScore
/// - RepositoryHealthIndex
/// </summary>
public sealed class RepositoryEvaluator : BaseArchitectureEvaluator
{
    private readonly RepositoryPolicy _policy;


    public override string Name =>
        "RepositoryEvaluator";


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
        new(
            @"\b(TODO|FIXME|HACK|XXX)\b",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);



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
        _policy =
            options.Value.Repository ??
            new RepositoryPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();


        if (!_policy.EnforceGovernanceFiles &&
            !_policy.EnforceCiPresence &&
            !_policy.CheckLargeFiles &&
            !_policy.CheckTodoDensity &&
            !_policy.RequireDependencyLock)
        {
            _logger.LogInformation(
                "⏭ {Evaluator} disabled by policy.",
                Name);

            return results;
        }



        var files =
            EnumerateRepositoryFiles(projectPath);



        _logger.LogInformation(
            "🏗️ Running {Evaluator} on repository {Path}",
            Name,
            projectPath);



        var governance =
            EvaluateGovernance(
                projectPath);


        var cicd =
            EvaluateCiCd(
                projectPath);


        var structure =
            EvaluateStructure(
                files);


        var maintainability =
            await EvaluateMaintainabilityAsync(
                files,
                token);


        var dependency =
            EvaluateDependencies(
                files);



        var health =
            ComputeRepositoryHealth(
                governance.Score,
                cicd.Score,
                structure.Score,
                dependency.Score,
                maintainability.Score);



        results.Add(
            new ArchitectureEvaluatorResult(
                Name,
                projectPath)
            {
                Category =
                    "Repository",

                Metrics =
                {
                    ["GovernanceScore"] =
                        governance.Score,

                    ["CICDScore"] =
                        cicd.Score,

                    ["StructureScore"] =
                        structure.Score,

                    ["DependencyScore"] =
                        dependency.Score,

                    ["MaintainabilityScore"] =
                        maintainability.Score,

                    ["MissingGovernanceFiles"] =
                        governance.MissingFiles,

                    ["LargeFileCount"] =
                        structure.LargeFiles,

                    ["TodoCount"] =
                        maintainability.TodoCount,

                    ["ProjectCount"] =
                        structure.ProjectCount,

                    ["RepositoryHealthIndex"] =
                        health
                },

                Metadata =
                {
                    ["Evaluator"] =
                        Name,

                    ["ProjectPath"] =
                        projectPath,

                    ["PolicyEnabled"] =
                        "True"
                }
            });



        _logger.LogInformation(
            "✅ {Evaluator} completed RepositoryHealthIndex={Health}",
            Name,
            health);



        return results;
    }



    private RepositoryMetric EvaluateGovernance(
        string projectPath)
    {
        if (!_policy.EnforceGovernanceFiles)
        {
            return RepositoryMetric.Success();
        }


        var violations = 0;
        var missing = 0;


        foreach (var required in _policy.RequiredFiles)
        {
            if (!File.Exists(
                    Path.Combine(
                        projectPath,
                        required)))
            {
                violations++;
                missing++;
            }
        }


        if (_policy.RequireContributingGuide &&
            !File.Exists(
                Path.Combine(
                    projectPath,
                    "CONTRIBUTING.md")))
        {
            violations++;
        }


        if (_policy.RequireVersionFile &&
            !File.Exists(
                    Path.Combine(
                        projectPath,
                        "CHANGELOG.md")) &&
            !File.Exists(
                    Path.Combine(
                        projectPath,
                        "version.json")))
        {
            violations++;
        }


        return new RepositoryMetric
        {
            Score =
                Clamp(
                    1 -
                    violations * 0.1),

            MissingFiles =
                missing
        };
    }



    private RepositoryMetric EvaluateCiCd(
        string projectPath)
    {
        if (!_policy.EnforceCiPresence)
        {
            return RepositoryMetric.Success();
        }


        var exists =
            _policy.CiPaths.Any(
                path =>
                    Directory.Exists(
                        Path.Combine(
                            projectPath,
                            path))
                    ||
                    File.Exists(
                        Path.Combine(
                            projectPath,
                            path)));


        return new RepositoryMetric
        {
            Score =
                exists
                    ? 1
                    : 0.6
        };
    }



    private RepositoryMetric EvaluateStructure(
        IReadOnlyCollection<string> files)
    {
        var score = 1d;


        var largeFiles =
            files.Count(
                file =>
                {
                    try
                    {
                        return new FileInfo(file).Length >
                               _policy.MaxFileSizeBytes;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(
                            ex,
                            "Unable to inspect file {File}",
                            file);

                        return false;
                    }
                });


        if (largeFiles > 0)
        {
            score -=
                Math.Min(
                    0.3,
                    largeFiles * 0.02);
        }


        var projects =
            files.Count(
                IsProjectFile);


        if (projects >
            _policy.MaxProjectsPerRepo)
        {
            score -= 0.15;
        }


        return new RepositoryMetric
        {
            Score =
                Clamp(score),

            LargeFiles =
                largeFiles,

            ProjectCount =
                projects
        };
    }



    private async Task<RepositoryMetric>
        EvaluateMaintainabilityAsync(
            IReadOnlyCollection<string> files,
            CancellationToken token)
    {
        if (!_policy.CheckTodoDensity)
        {
            return RepositoryMetric.Success();
        }


        var sourceFiles =
            files.Where(
                    IsSourceFile)
                .ToList();


        var todoCount = 0;


        foreach (var file in sourceFiles)
        {
            token.ThrowIfCancellationRequested();


            try
            {
                var content =
                    await File.ReadAllTextAsync(
                        file,
                        token);


                todoCount +=
                    TodoRx.Matches(content)
                        .Count;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Unable to inspect source file {File}",
                    file);
            }
        }


        var score = 1d;


        if (sourceFiles.Count > 0)
        {
            var density =
                todoCount /
                (double)sourceFiles.Count;


            if (density >= _policy.MaxTodoDensity)
            {
                score -= 0.15;
            }
        }


        return new RepositoryMetric
        {
            Score =
                Clamp(score),

            TodoCount =
                todoCount
        };
    }



    private RepositoryMetric EvaluateDependencies(
        IReadOnlyCollection<string> files)
    {
        if (!_policy.RequireDependencyLock)
        {
            return RepositoryMetric.Success();
        }


        var hasLock =
            files.Any(
                file =>
                    DependencyLockFiles.Any(
                        lockFile =>
                            file.EndsWith(
                                lockFile,
                                StringComparison.OrdinalIgnoreCase)));


        return new RepositoryMetric
        {
            Score =
                hasLock
                    ? 1
                    : 0.8
        };
    }



    private static List<string> EnumerateRepositoryFiles(
        string path)
    {
        return Directory
            .EnumerateFiles(
                path,
                "*.*",
                SearchOption.AllDirectories)
            .Where(
                f =>
                    !IsExcludedDir(f))
            .ToList();
    }



    private static bool IsProjectFile(
        string file)
    {
        return
            file.EndsWith(
                ".csproj",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(
                "package.json",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(
                "pom.xml",
                StringComparison.OrdinalIgnoreCase);
    }



    private static bool IsSourceFile(
        string file)
    {
        return
            file.EndsWith(".cs",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".java",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".ts",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".js",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".py",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".cpp",
                StringComparison.OrdinalIgnoreCase)
            ||
            file.EndsWith(".h",
                StringComparison.OrdinalIgnoreCase);
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
                maintainability * 0.15)
            * 100,
            2);
    }



    private static double Clamp(
        double value)
        =>
            Math.Max(
                0,
                Math.Min(
                    1,
                    value));



    private sealed class RepositoryMetric
    {
        public double Score { get; init; }

        public int MissingFiles { get; init; }

        public int LargeFiles { get; init; }

        public int TodoCount { get; init; }

        public int ProjectCount { get; init; }


        public static RepositoryMetric Success()
            =>
                new()
                {
                    Score = 1
                };
    }
}