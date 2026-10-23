using Aegis.Architecture.Analysis;
using Aegis.Architecture.RuleEngines;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Services;

/// <summary>
/// Coordinates the complete deterministic Aegis architecture analysis pipeline.
///
/// Pipeline:
/// Context Detection
///        ↓
/// Evaluators (Facts)
///        ↓
/// RuleEngineCore (Violations)
///        ↓
/// Weighting
///        ↓
/// Aggregation
///        ↓
/// Architecture Report
/// </summary>
public sealed class AegisArchitectureAnalyzer
{
    private readonly RuleEngine _ruleEngine;
    private readonly ILogger<AegisArchitectureAnalyzer> _logger;


    public AegisArchitectureAnalyzer(
        RuleEngine ruleEngine,
        ILogger<AegisArchitectureAnalyzer> logger)
    {
        _ruleEngine = ruleEngine;
        _logger = logger;
    }



    public async Task<AegisArchitectureReport> AnalyzeAsync(
        string projectPath,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);


        _logger.LogInformation(
            "Starting Aegis architecture analysis for {Path}",
            projectPath);



        token.ThrowIfCancellationRequested();



        var context =
            ProjectArchitectureContextDetector
                .Detect(projectPath);



        _logger.LogInformation(
            "Architecture context detected: {Language}/{Framework} - {Architecture}",
            context.Language,
            context.Framework ?? "Unknown",
            context.ArchitectureStyle ?? "Unknown");



        var report =
            await _ruleEngine.RunAsync(
                projectPath,
                context,
                token);

        if (context.DiscoveryDiagnostics.Count > 0)
        {
            report.Diagnostics.AddRange(context.DiscoveryDiagnostics);
            report.Metrics.AssessmentComplete = false;
        }

        EnrichReportContext(
            report,
            context);



        report.Facts.Add(
            BuildContextFact(context));



        // RuleEngine aggregation already computed compliance from actual rule checks,
        // including passing outcomes and coverage. Recomputing from findings alone here
        // would discard those scores and treat categories without findings as passing.



        _logger.LogInformation(
            "Aegis analysis completed for {Project}. Health={Health:0.00}%",
            report.ProjectName,
            report.Metrics.ProjectHealthIndex);



        return report;
    }



    private static void EnrichReportContext(
        AegisArchitectureReport report,
        ProjectArchitectureContext context)
    {
        report.ProjectName =
            context.ProjectName;

        report.ProjectPath =
            context.RootPath;

        report.Language =
            context.Language;

        report.Framework =
            context.Framework;

        report.TotalFilesScanned =
            context.FileCount;
    }



    private static ArchitectureEvaluatorResult BuildContextFact(
        ProjectArchitectureContext context)
    {
        return new ArchitectureEvaluatorResult(
            "ProjectContextFact",
            context.RootPath)
        {
            Category = "Context",

            Domain =
                context.DomainType
                ?? "General",

            Metrics =
            {
                ["Confidence"] =
                    context.Confidence,

                ["FileCount"] =
                    context.FileCount,

                ["LinesOfCode"] =
                    context.LinesOfCode,

                ["AverageComplexity"] =
                    context.AverageComplexity,

                ["DiscoveryDiagnosticCount"] =
                    context.DiscoveryDiagnostics.Count
            },

            Metadata =
            {
                ["Language"] =
                    context.Language,

                ["Framework"] =
                    context.Framework
                    ?? "Unknown",

                ["DomainType"] =
                    context.DomainType
                    ?? "Unknown",

                ["Layer"] =
                    context.Layer
                    ?? "Unknown",

                ["ArchitectureStyle"] =
                    context.ArchitectureStyle
                    ?? "Unknown",

                ["Nature"] =
                    context.Nature
                    ?? "Generic",

                ["BuildSystem"] =
                    context.BuildSystem.ToString(),

                ["TargetRuntime"] =
                    context.TargetRuntime
                    ?? "Unknown",

                ["DetectorVersion"] =
                    context.DetectorVersion,

                ["DetectionStrategy"] =
                    context.DetectionStrategy,

                ["Deployable"] =
                    context.IsDeployable.ToString(),

                ["Dockerized"] =
                    context.IsDockerized.ToString(),

                ["Kubernetes"] =
                    context.UsesKubernetes.ToString(),

                ["Confidence"] =
                    context.Confidence.ToString("P0"),

                ["DiscoveryDiagnosticCount"] =
                    context.DiscoveryDiagnostics.Count.ToString(),

                ["AnalysisStatus"] = context.DiscoveryDiagnostics.Count == 0
                    ? "Completed"
                    : "CompletedWithDiagnostics",

                ["Diagnostics"] = string.Join(
                    Environment.NewLine,
                    context.DiscoveryDiagnostics.Select(diagnostic =>
                        $"{diagnostic.Code}: {diagnostic.Message}"))
            }
        };
    }
}