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
/// Evaluates dependency metadata from detected dependency files.
///
/// The evaluator does not resolve package versions against external registries.
/// It produces deterministic dependency facts consumed by the RuleEngine.
///
/// Responsibilities:
/// - Detect dependency manifests
/// - Extract declared versions
/// - Calculate dependency metrics
///
/// Future registry comparison belongs to a dedicated dependency intelligence service.
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
        "DotNet",
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
                .Where(IsDependencyFile)
                .ToList();



        if (files.Count == 0)
        {
            return results;
        }



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
                    $"Unable to read dependency file {file}.",
                    ex);

                continue;
            }



            var versions =
                VersionRegex.Matches(content);



            if (versions.Count == 0)
                continue;



            results.Add(
                new ArchitectureEvaluatorResult(
                    Name,
                    file)
                {
                    ProjectName =
                        Context.ProjectName,

                    Language =
                        Context.Language,

                    Framework =
                        Context.Framework,

                    DetectionConfidence =
                        Context.Confidence,

                    Category =
                        nameof(
                            ArchitectureRuleCategory.Dependency),


                    Metrics =
                    {
                        ["DeclaredVersionCount"] =
                            versions.Count,

                        ["ManifestDetected"] =
                            1
                    },


                    Metadata =
                    {
                        ["DependencyFile"] =
                            Path.GetFileName(file),

                        ["PackageCount"] =
                            versions.Count.ToString(),

                        ["Language"] =
                            Context.Language,

                        ["Framework"] =
                            Context.Framework
                            ?? "Unknown"
                    }
                });
        }



        results.Add(
            new ArchitectureEvaluatorResult(
                Name,
                Context.ProjectName)
            {
                ProjectName =
                    Context.ProjectName,

                Language =
                    Context.Language,

                Framework =
                    Context.Framework,

                Category =
                    "DependencySummary",


                Metrics =
                {
                    ["ManifestCount"] =
                        files.Count,

                    ["VersionDeclarations"] =
                        results.Sum(
                            x =>
                                x.Metrics
                                 .GetValueOrDefault(
                                     "DeclaredVersionCount"))
                },


                Metadata =
                {
                    ["Language"] =
                        Context.Language,

                    ["Framework"] =
                        Context.Framework
                        ?? "Unknown"
                }
            });



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Dependency evaluation completed with {results.Count} entries.");



        return results;
    }



    private static bool IsDependencyFile(
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