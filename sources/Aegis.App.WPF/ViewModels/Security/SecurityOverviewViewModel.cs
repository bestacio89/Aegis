using Aegis.Shared.Security.Enums;
using Aegis.Wpf.Models.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Windows;

namespace Aegis.Wpf.ViewModels.Security;

public sealed partial class SecurityOverviewViewModel : ObservableObject
{
    [ObservableProperty] private string projectName = "-";
    [ObservableProperty] private string verdict = "-";
    [ObservableProperty] private bool isCompliant;
    [ObservableProperty] private string riskLabel = "-";
    [ObservableProperty] private double complianceRate;
    [ObservableProperty] private string maxSeverity = "-";
    [ObservableProperty] private int totalFindings;
    [ObservableProperty] private int failedFindings;
    [ObservableProperty] private int domainsAssessed;
    [ObservableProperty] private string duration = "-";
    [ObservableProperty] private string executiveMessage = "No security scan loaded.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoSeverityData))]
    private bool hasSeverityData;

    [ObservableProperty] private ISeries[] severitySeries = [];
    [ObservableProperty] private ISeries[] domainSeries = [];
    [ObservableProperty] private Axis[] domainXAxes = [];
    [ObservableProperty] private Axis[] domainYAxes = [];

    public bool NoSeverityData => !HasSeverityData;

    public void Update(SecurityScanResult result)
    {
        ProjectName = result.ProjectName;
        IsCompliant = result.IsCompliant;
        Verdict = result.IsCompliant ? "PASS" : "FAIL";
        RiskLabel = result.RiskLabel;
        ComplianceRate = result.ComplianceRate;
        MaxSeverity = result.MaxSeverity.ToString();
        TotalFindings = result.Findings.Count;
        FailedFindings = result.Findings.Count(f => f.Severity >= SecuritySeverity.Medium);
        DomainsAssessed = result.Domains.Count;
        Duration = $"{result.DurationSeconds:0.0}s";

        var atRisk = result.Domains.Count(d => !d.IsCompliant);
        ExecutiveMessage = FailedFindings == 0
            ? $"No material findings. {DomainsAssessed} domain(s) assessed, overall risk {RiskLabel}."
            : $"{FailedFindings} finding(s) at Medium severity or above; {atRisk} of {DomainsAssessed} domain(s) not compliant. Highest severity: {MaxSeverity}.";

        BuildSeverityChart(result);
        BuildDomainChart(result);
    }

    private void BuildSeverityChart(SecurityScanResult result)
    {
        var slices = result.SeverityCounts.Where(c => c.Count > 0).ToList();
        HasSeverityData = slices.Count > 0;

        SeveritySeries = slices
            .Select(c => (ISeries)new PieSeries<int>
            {
                Values = new[] { c.Count },
                Name = c.Severity.ToString(),
                Fill = new SolidColorPaint(ToSkColor(SecuritySeverityPalette.Rgb(c.Severity)))
            })
            .ToArray();
    }

    private void BuildDomainChart(SecurityScanResult result)
    {
        var labels = result.Domains.Select(d => d.Domain.ToString()).ToArray();
        var rates = result.Domains.Select(d => d.ComplianceRate).ToArray();

        DomainSeries =
        [
            new ColumnSeries<double>
            {
                Values = rates,
                Name = "Compliance (%)",
                Fill = new SolidColorPaint(SKColors.DeepSkyBlue),
                Padding = 10
            }
        ];

        DomainXAxes =
        [
            new Axis
            {
                Labels = labels,
                LabelsRotation = 15,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray)
            }
        ];

        DomainYAxes =
        [
            new Axis
            {
                MinLimit = 0,
                MaxLimit = 100,
                MinStep = 20,
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                SeparatorsPaint = new SolidColorPaint(new SKColor(0x22, 0x29, 0x3A)) { StrokeThickness = 1 }
            }
        ];
    }

    private static SKColor ToSkColor(int rgb) =>
        new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}