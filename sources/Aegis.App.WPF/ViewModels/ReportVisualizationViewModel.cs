using Aegis.Wpf.Models;
using Aegis.Shared.Architecture.Models;

using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.ObjectModel;


namespace Aegis.Wpf.ViewModels;

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



        HealthStatus =
            health switch
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



        SummaryItems.Add(
            new(
                "Rules evaluated",
                report.Results.Count.ToString(),
                "Architecture rules processed by the rule engine"));



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
                $"{report.Metrics.WeightedCompliance * 100:0.##}%",
                "Weighted rule compliance score"));



        SummaryItems.Add(
            new(
                "Health index",
                $"{health:0.##}%",
                "Aggregated architecture health score"));
    }



    // ==========================================================
    // HELPERS
    // ==========================================================

    private static string BuildExecutiveMessage(
        AegisArchitectureReport report,
        double health)
    {
        if (report.TotalViolations == 0)
        {
            return
                "Architecture evaluation completed successfully. No violations detected.";
        }


        return
            $"Architecture review detected {report.TotalViolations} findings. " +
            $"Current health index is {health:0.##}%.";
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