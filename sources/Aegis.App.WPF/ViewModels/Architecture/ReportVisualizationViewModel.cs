using Aegis.Wpf.Models;
using Aegis.Shared.Architecture.Models;

using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.ObjectModel;


namespace Aegis.Wpf.ViewModels.Architecture;

public sealed partial class ReportVisualizationViewModel
    : ObservableObject
{
    public ReportVisualizationViewModel()
    {
        SummaryItems =
            new ObservableCollection<VisualizationSummaryItem>();
    }



    // ==========================================================
    // SUMMARY
    // ==========================================================

    public ObservableCollection<VisualizationSummaryItem>
        SummaryItems
    {
        get;
    }



    [ObservableProperty]
    private string executiveMessage =
        "No architecture report loaded.";



    [ObservableProperty]
    private string projectName =
        "-";



    [ObservableProperty]
    private string healthStatus =
        "-";



    [ObservableProperty]
    private string architectureSummary =
        "-";



    // ==========================================================
    // UPDATE FROM REPORT
    // ==========================================================

    public void Update(
        AegisArchitectureReport report,
        ProjectArchitectureContext? context = null)
    {
        SummaryItems.Clear();



        ProjectName =
            string.IsNullOrWhiteSpace(report.ProjectName)
                ? "-"
                : report.ProjectName;



        var health =
            report.Metrics.ProjectHealthIndex;


        // Nothing was compared against anything: the scores below are not a verdict.
        var notAssessed =
            report.EvaluationStats is not null
            && report.ChecksPerformed == 0;



        HealthStatus =
            notAssessed
                ? "Not assessed"
                : health switch
                {
                    >= 90 => "Excellent",
                    >= 80 => "Healthy",
                    >= 50 => "At Risk",
                    _ => "Critical"
                };



        ExecutiveMessage =
            BuildExecutiveMessage(
                report,
                health);



        ArchitectureSummary =
            BuildArchitectureSummary(
                report,
                context);



        SummaryItems.Add(
            new(
                "Files scanned",
                report.TotalFilesScanned.ToString(),
                "Files evaluated during analysis"));



        SummaryItems.Add(
            new(
                "Facts collected",
                report.TotalFacts.ToString(),
                "Raw architecture observations produced by evaluators"));



        // Results only lists violations, so Results.Count is NOT the number of rules evaluated.
        // The engine reports what it really applied.
        if (report.EvaluationStats is not null)
        {
            SummaryItems.Add(
                new(
                    "Rules evaluated",
                    $"{report.RulesEvaluated} / {report.RulesRegistered}",
                    "Rules that matched a measurement from this project, of those registered"));


            SummaryItems.Add(
                new(
                    "Checks performed",
                    report.ChecksPerformed.ToString("N0"),
                    "Rule-versus-measurement comparisons"));


            SummaryItems.Add(
                new(
                    "Not applicable",
                    report.EvaluationStats.RulesNotApplicable.Count.ToString(),
                    "Rules excluded by detected technology scope"));

            var unevaluatedInScope = Math.Max(
                0,
                report.EvaluationStats.RulesRegistered
                - report.EvaluationStats.RulesEvaluated
                - report.EvaluationStats.RulesNotApplicable.Count);

            SummaryItems.Add(
                new(
                    "Missing measurements",
                    unevaluatedInScope.ToString(),
                    "In-scope rules without a matching metric contract"));

            SummaryItems.Add(
                new(
                    "Rule evaluation coverage",
                    report.Metrics.HasCoverageEvidence
                        ? $"{report.Metrics.RuleEvaluationCoverage:0.##}%"
                        : "n/a",
                    "Share of applicable rules evaluated; unsupported rules remain in the denominator"));

            SummaryItems.Add(
                new(
                    "Coverage-adjusted health",
                    report.Metrics.HasAssessmentEvidence && report.Metrics.HasCoverageEvidence
                        ? $"{report.Metrics.CoverageAdjustedHealthIndex:0.##}%"
                        : "n/a",
                    "Observed health discounted by rule-evaluation coverage; diagnostic, not compliance"));

            SummaryItems.Add(
                new(
                    "Assessment completeness",
                    report.Metrics.AssessmentComplete ? "Complete" : "Incomplete",
                    "Incomplete means applicable rules or evidence remain unresolved"));
        }
        else
        {
            SummaryItems.Add(
                new(
                    "Rules evaluated",
                    report.Results.Count.ToString(),
                    "Architecture rules processed by the rule engine"));
        }



        SummaryItems.Add(
            new(
                "Violations",
                report.TotalViolations.ToString(),
                "Detected architectural deviations"));



        SummaryItems.Add(
            new(
                "Domains",
                report.Domains.Count.ToString(),
                "Architecture domains evaluated"));



        SummaryItems.Add(
            new(
                "Compliance",
                notAssessed
                    ? "n/a"
                    : $"{report.Metrics.WeightedCompliance * 100:0.##}%",
                notAssessed
                    ? "No rule could be evaluated"
                    : "Share of weighted checks that passed"));



        SummaryItems.Add(
            new(
                "Health index",
                notAssessed
                    ? "n/a"
                    : $"{health:0.##}%",
                notAssessed
                    ? "No rule could be evaluated"
                    : "Aggregated architecture health score"));
    }



    // ==========================================================
    // HELPERS
    // ==========================================================

    private static string BuildExecutiveMessage(
        AegisArchitectureReport report,
        double health)
    {
        var stats = report.EvaluationStats;

        if (stats is not null && stats.ChecksPerformed == 0)
        {
            if (stats.RulesNotApplicable.Count == stats.RulesRegistered)
            {
                return "No architecture rules applied to the detected project technology. This is not a compliance verdict.";
            }

            var missingMetrics = Math.Max(0, stats.RulesRegistered - stats.RulesNotApplicable.Count);
            return
                "No rule could be evaluated: none of the measurements produced by this analysis matched a " +
                $"rule in scope ({missingMetrics} in-scope rule(s), {stats.RulesNotApplicable.Count} not applicable). This is not a clean result.";
        }


        var notApplicable = stats?.RulesNotApplicable.Count ?? 0;
        var notEvaluated = stats is null
            ? 0
            : Math.Max(0, stats.RulesRegistered - stats.RulesEvaluated - notApplicable);

        var coverage = stats is null
            ? string.Empty
            : $" {notApplicable} rule(s) were not applicable; {notEvaluated} applicable rule(s) had no matching measurement.";

        var assessmentQualifier = stats is null || report.Metrics.AssessmentComplete
            ? string.Empty
            : $" Assessment is incomplete: {report.Metrics.RuleEvaluationCoverage * 100:0.##}% rule coverage; the health index is provisional.";


        if (report.TotalViolations == 0)
        {
            return stats is null
                ? "Architecture evaluation completed successfully. No violations detected."
                : $"Architecture evaluation completed. No violations across {stats.ChecksPerformed:N0} checks " +
                  $"({stats.RulesEvaluated} of {stats.RulesRegistered} rules applied).{coverage}{assessmentQualifier}";
        }


        return
            $"Architecture review detected {report.TotalViolations} findings. " +
            $"Observed health index is {health:0.##}%.{coverage}{assessmentQualifier}";
    }



    private static string BuildArchitectureSummary(
        AegisArchitectureReport report,
        ProjectArchitectureContext? context)
    {
        var language =
            string.IsNullOrWhiteSpace(report.Language)
                ? "Unknown"
                : report.Language;



        var framework =
            string.IsNullOrWhiteSpace(report.Framework)
                ? "Unknown"
                : report.Framework;



        var architecture =
            context?.ArchitectureStyle
            ?? report.Facts
                .SelectMany(x => x.Metadata)
                .FirstOrDefault(x =>
                    x.Key.Equals(
                        "ArchitectureStyle",
                        StringComparison.OrdinalIgnoreCase))
                .Value
            ?? "Unknown";



        return
            $"{language}/{framework} - {architecture}";
    }
}