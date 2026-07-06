using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.IO;
using System.Text.Json;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class SectionDashboardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "🧩 Aegis Policy Sections";

    // LiveCharts replaces PlotModel entirely
    [ObservableProperty]
    private ISeries[] _series = Array.Empty<ISeries>();

    [ObservableProperty]
    private Axis[] _xAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] _yAxes = Array.Empty<Axis>();

    public SectionDashboardViewModel()
    {
        LoadPolicySections();
    }

    private void LoadPolicySections()
    {
        try
        {
            var policyPath = Path.Combine(AppContext.BaseDirectory, "config", "aegis.policy.json");

            if (!File.Exists(policyPath))
            {
                BuildEmpty("Policy file not found");
                return;
            }

            var json = File.ReadAllText(policyPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("AegisPolicy", out var root))
            {
                BuildEmpty("Invalid policy structure");
                return;
            }

            var names = new List<string>();
            var counts = new List<int>();

            foreach (var prop in root.EnumerateObject())
            {
                names.Add(prop.Name);
                counts.Add(prop.Value.EnumerateObject().Count());
            }

            BuildChart(names, counts);
        }
        catch
        {
            BuildEmpty("Failed to load policy data");
        }
    }

    // ---------------- LiveCharts build ----------------

    private void BuildChart(List<string> names, List<int> counts)
    {
        Series = new ISeries[]
        {
            new ColumnSeries<int>
            {
                Values = counts,
                Name = "Rules per Section",
                Fill = new SolidColorPaint(new SKColor(0, 191, 255)),
                Stroke = new SolidColorPaint(new SKColor(255, 255, 255)) { StrokeThickness = 1 }
            }
        };

        XAxes = new Axis[]
        {
            new Axis
            {
                Labels = names,
                Name = "Sections",
                LabelsPaint = new SolidColorPaint(SKColors.White)
            }
        };

        YAxes = new Axis[]
        {
            new Axis
            {
                Name = "Rule Count",
                LabelsPaint = new SolidColorPaint(SKColors.White)
            }
        };
    }

    private void BuildEmpty(string message)
    {
        Title = message;

        Series = Array.Empty<ISeries>();
        XAxes = Array.Empty<Axis>();
        YAxes = Array.Empty<Axis>();
    }
}