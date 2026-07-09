using Aegis.App.Wpf.models;
using Aegis.App.Wpf.Models;
using Aegis.App.Wpf.Services.Abstractions;
using Aegis.App.Wpf.ViewModels;
using Aegis.Shared.Architecture.Models;
using System.IO;

namespace Aegis.App.Wpf.Services;

public static class DashboardSnapshotBuilder
{
    public static DashboardSnapshot Build(
        AegisArchitectureReport report,
        ProjectArchitectureContext context)
    {
        return new DashboardSnapshot
        {
            Layers = BuildLayers(report),

            Rules = BuildRules(report),

            Sections = BuildSections(report),

            Metrics = BuildMetrics(
                report,
                context)
        };
    }



    private static IReadOnlyList<LayerStat> BuildLayers(
        AegisArchitectureReport report)
    {
        return report.Results
            .GroupBy(x => x.Domain ?? "Unknown")
            .Select(x =>
                new LayerStat(
                    x.Key,
                    x.Count()))
            .ToList();
    }



    private static IReadOnlyList<RuleDashboardItem> BuildRules(
        AegisArchitectureReport report)
    {
        return report.Results
            .Select(x =>
                new RuleDashboardItem(
                    x.RuleName,
                    x.Category,
                    x.Severity,
                    Path.GetFileName(x.Target),
                    x.Message,
                    x.WeightedImpact))
            .ToList();
    }



    private static IReadOnlyList<SectionDashboardItem> BuildSections(
        AegisArchitectureReport report)
    {
        return report.ComplianceScores
            .Select(x =>
                new SectionDashboardItem(
                    x.Key.ToString(),
                    x.Key,
                    x.Value,
                    0,
                    x.Value >= 80
                        ? "Compliant"
                        : "Needs Review",
                    ""))
            .ToList();
    }



    private static DashboardMetrics BuildMetrics(
        AegisArchitectureReport report,
        ProjectArchitectureContext context)
    {
        var compliance =
            report.ComplianceScores.Count == 0
                ? 100
                : report.ComplianceScores.Values.Average();


        return new DashboardMetrics(
            report.ProjectName,
            report.TotalFilesScanned,
            report.TotalViolations,
            compliance,
            context.Confidence,
            context.Language ?? "Unknown",
            context.Framework ?? "Unknown",
            context.ArchitectureStyle ?? "Unknown",
            context.Layer ?? "Unknown",
            report.ScanDate);
    }
}