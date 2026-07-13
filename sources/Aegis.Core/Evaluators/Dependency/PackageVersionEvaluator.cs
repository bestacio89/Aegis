using System.Text.RegularExpressions;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Dependency;
using Aegis.Shared.Diagnostics;

namespace Aegis.Architecture.Evaluators.Dependency;


/// <summary>
/// Evaluates dependency version metadata discovered by architecture detectors.
///
/// The evaluator consumes dependency manifests already identified during
/// project analysis and produces deterministic dependency facts.
///
/// Responsibilities:
/// - Extract declared versions
/// - Calculate dependency metrics
///
/// Registry comparison belongs to dependency intelligence services.
/// </summary>
public sealed class PackageVersionEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly DependencyPolicy _policy;



    private static readonly Regex VersionRegex =
        new(
            @"(\d+)\.(\d+)\.(\d+)",
            RegexOptions.Compiled);



    public override string Name =>
        "PackageVersionEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "JavaScript",
        "TypeScript",
        "Python"
    ];



    public override string[] SupportedFrameworks =>
    [
        ".Net",
        "Node",
        "Python"
    ];



    public PackageVersionEvaluator(
        ILogger<PackageVersionEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Dependency
            ?? new DependencyPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        if (Context is null)
            return results;



        if (!_policy.Enabled ||
            !_policy.CheckOutdatedPackages)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Trace,
                "Dependency evaluation disabled by policy.");

            return results;
        }



        var files =
            EnumerateApplicationFiles(projectPath)
                .Where(IsDependencyManifest)
                .ToList();



        if (files.Count == 0)
            return results;



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Analyzing {files.Count} dependency manifests.");



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();



            string content;

            try
            {
                content =
                    await File.ReadAllTextAsync(
                        file,
                        token);
            }
            catch (Exception ex)
            {
                AegisDiagnostics.Report(
                    Name,
                    DiagnosticLevel.Warning,
                    $"Unable to read dependency manifest {file}.",
                    ex);

                continue;
            }



            var versions =
                VersionRegex.Matches(content);



            results.Add(
                CreateResult(
                    file,
                    versions.Count));
        }



        results.Add(
            CreateSummary(
                projectPath,
                files.Count,
                results));



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Dependency evaluation completed with {results.Count} entries.");



        return results;
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        int versionCount)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            ProjectName =
                Context?.ProjectName,

            Language =
                Context?.Language,

            Framework =
                Context?.Framework,

            DetectionConfidence =
                Context?.Confidence ?? 0,

            Category =
                nameof(ArchitectureRuleCategory.Dependency),


            Metrics =
            {
                ["DeclaredVersionCount"] =
                    versionCount,

                ["ManifestDetected"] =
                    1
            },


            Metadata =
            {
                ["FilePath"] = file,

                ["DependencyFile"] =
                    Path.GetFileName(file),

                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown"
            }
        };
    }



    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        int manifestCount,
        IEnumerable<ArchitectureEvaluatorResult> results)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            ProjectName =
                Context?.ProjectName,

            Language =
                Context?.Language,

            Framework =
                Context?.Framework,

            Category =
                "DependencySummary",


            Metrics =
            {
                ["ManifestCount"] =
                    manifestCount,

                ["VersionDeclarations"] =
                    results.Sum(
                        x =>
                            x.Metrics.GetValueOrDefault(
                                "DeclaredVersionCount"))
            },


            Metadata =
            {
                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown"
            }
        };
    }



    private static bool IsDependencyManifest(
        string file)
    {
        return
            file.EndsWith(
                "packages.config",
                StringComparison.OrdinalIgnoreCase)

            ||

            file.EndsWith(
                "package.json",
                StringComparison.OrdinalIgnoreCase)

            ||

            file.EndsWith(
                "requirements.txt",
                StringComparison.OrdinalIgnoreCase);
    }
}