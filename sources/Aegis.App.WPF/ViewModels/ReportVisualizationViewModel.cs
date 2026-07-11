using Aegis.App.Wpf.Models;
using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class ReportVisualizationViewModel
    : ObservableObject
{
    public ReportVisualizationViewModel()
    {
        SummaryItems =
            new ObservableCollection<VisualizationSummaryItem>();
    }



    public ObservableCollection<VisualizationSummaryItem>
        SummaryItems
    { get; }



    [ObservableProperty]
    private string executiveMessage =
        "No architecture report loaded.";



    [ObservableProperty]
    private string projectName =
        "-";



    [ObservableProperty]
    private string healthStatus =
        "-";



    /// <summary>
    /// Populated directly from the live AegisArchitectureReport / ProjectArchitectureContext
    /// rather than the unused DashboardSnapshot pipeline (DashBoardSnapshotBuilder has no
    /// live consumers anywhere in the app — see RefreshDashboards in MainViewModel, which
    /// reads the same two live objects for Layers/Sections rather than that dead builder).
    /// </summary>
    public void Update(
        AegisArchitectureReport report,
        ProjectArchitectureContext context)
    {
        SummaryItems.Clear();



        ProjectName =
            report.ProjectName ?? "-";




        ExecutiveMessage =
            report.TotalViolations == 0
                ? "Architecture compliance is healthy. No violations detected."
                : $"Architecture review detected {report.TotalViolations} findings requiring attention.";



        var healthIndex =
            report.Metrics?.ProjectHealthIndex ?? 0;

        HealthStatus =
            healthIndex >= 80
                ? "Compliant"
                : healthIndex >= 50
                    ? "At Risk"
                    : "Critical";



        SummaryItems.Add(
            new(
                "Files scanned",
                report.TotalFilesScanned.ToString(),
                "Source files analyzed by Aegis"));



        SummaryItems.Add(
            new(
                "Violations",
                report.TotalViolations.ToString(),
                "Architecture rule violations"));



        SummaryItems.Add(
            new(
                "Framework",
                context.Framework ?? "Unknown",
                "Detected technology stack"));



        SummaryItems.Add(
            new(
                "Architecture",
                context.ArchitectureStyle ?? "Unknown",
                "Detected architectural pattern"));



        SummaryItems.Add(
            new(
                "Confidence",
                $"{context.Confidence:P0}",
                "Detection confidence score"));



        SummaryItems.Add(
            new(
                "Health index",
                $"{healthIndex:0.##}%",
                "Weighted compliance across all evaluated categories"));
    }
}