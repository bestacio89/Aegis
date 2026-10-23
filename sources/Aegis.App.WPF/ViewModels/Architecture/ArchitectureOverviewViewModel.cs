using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// Presents the assessment verdict and its evidence quality without conflating
/// compliance, health, and evaluation coverage.
/// </summary>
public sealed partial class ArchitectureOverviewViewModel : ObservableObject
{
    [ObservableProperty] private string projectName = "No assessment loaded";
    [ObservableProperty] private string assessmentStatus = "Not assessed";
    [ObservableProperty] private string assessmentMessage = "Select a repository and run an architecture assessment to populate this overview.";
    [ObservableProperty] private string findings = "—";
    [ObservableProperty] private string criticalFindings = "—";
    [ObservableProperty] private string rulesEvaluated = "—";
    [ObservableProperty] private string ruleCoverage = "n/a";
    [ObservableProperty] private string healthIndex = "n/a";
    [ObservableProperty] private string weightedCompliance = "n/a";
    [ObservableProperty] private string filesScanned = "—";
    [ObservableProperty] private string excludedPaths = "n/a";
    [ObservableProperty] private string nextAction = "Run an assessment to see evidence-backed recommendations.";

    public void Update(AegisArchitectureReport report, ProjectArchitectureContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        ProjectName = string.IsNullOrWhiteSpace(report.ProjectName) ? "Unknown project" : report.ProjectName;
        Findings = report.TotalViolations.ToString("N0");
        CriticalFindings = report.Results.Count(x =>
            x.Severity is Aegis.Shared.Architecture.Enums.ArchitectureRuleSeverity.Blocker
                or Aegis.Shared.Architecture.Enums.ArchitectureRuleSeverity.Critical).ToString("N0");
        FilesScanned = report.TotalFilesScanned.ToString("N0");

        var stats = report.EvaluationStats;
        var hasNoChecks = stats is not null && stats.ChecksPerformed == 0;
        AssessmentStatus = hasNoChecks
            ? "Not assessed"
            : report.Metrics.AssessmentComplete ? "Assessment complete" : "Provisional";

        RulesEvaluated = stats is null
            ? report.Results.Count.ToString("N0")
            : $"{report.RulesEvaluated:N0} / {report.RulesRegistered:N0}";

        RuleCoverage = report.Metrics.HasCoverageEvidence
            ? $"{report.Metrics.RuleEvaluationCoverage:0.#}%"
            : "n/a";
        HealthIndex = !hasNoChecks && report.Metrics.HasAssessmentEvidence
            ? $"{report.Metrics.ProjectHealthIndex:0.#}%"
            : "n/a";
        WeightedCompliance = !hasNoChecks && report.Metrics.HasAssessmentEvidence
            ? $"{report.Metrics.WeightedCompliance * 100:0.#}%"
            : "n/a";

        var contextFact = report.Facts.FirstOrDefault(f =>
            string.Equals(f.Source, "ProjectContextFact", StringComparison.OrdinalIgnoreCase));
        excludedPaths = contextFact is null
            ? "n/a"
            : contextFact.Metrics.GetValueOrDefault("ExcludedPathEntryCount").ToString("N0");

        AssessmentMessage = hasNoChecks
            ? "No rule checks were completed. This is not a clean assessment; inspect metric availability and analysis diagnostics."
            : report.Metrics.AssessmentComplete
                ? $"Assessment completed with {report.TotalViolations:N0} finding(s) across {report.ChecksPerformed:N0} checks."
                : $"Assessment is provisional. {RuleCoverage} rule coverage is available; health and compliance must be interpreted with that limitation.";

        NextAction = hasNoChecks
            ? "Investigate missing measurements and rule applicability before interpreting the health index."
            : !report.Metrics.AssessmentComplete
                ? "Resolve incomplete rule coverage and review analysis diagnostics before treating this result as a verdict."
                : report.TotalViolations > 0
                    ? "Start with blocker and critical findings, then inspect their evidence and affected source locations."
                    : "Review the evidence and excluded-source summary before accepting the assessment as complete.";
    }
}
