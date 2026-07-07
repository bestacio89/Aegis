using Aegis.App.Wpf.Models;
using Aegis.App.Wpf.Services.Abstractions;
using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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



    public void Update(
        DashboardSnapshot snapshot)
    {
        SummaryItems.Clear();



        var metrics =
            snapshot.Metrics;



        ProjectName =
            metrics.ProjectName ?? "-";




        ExecutiveMessage =
            metrics.TotalViolations == 0
                ? "Architecture compliance is healthy. No violations detected."
                : $"Architecture review detected {metrics.TotalViolations} findings requiring attention.";



        SummaryItems.Add(
            new(
                "Files scanned",
                metrics.FilesScanned.ToString(),
                "Source files analyzed by Aegis"));



        SummaryItems.Add(
            new(
                "Violations",
                metrics.TotalViolations.ToString(),
                "Architecture rule violations"));



        SummaryItems.Add(
            new(
                "Framework",
                metrics.Framework ?? "Unknown",
                "Detected technology stack"));



        SummaryItems.Add(
            new(
                "Architecture",
                metrics.ArchitectureStyle ?? "Unknown",
                "Detected architectural pattern"));



        SummaryItems.Add(
            new(
                "Confidence",
                $"{metrics.ArchitectureConfidence:P0}",
                "Detection confidence score"));
    }
}