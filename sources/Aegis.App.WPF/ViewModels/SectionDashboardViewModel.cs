using CommunityToolkit.Mvvm.ComponentModel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.IO;
using System.Text.Json;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class SectionDashboardViewModel : ObservableObject
{
    // 🧩 This is YOUR property (not an OxyPlot type)
    [ObservableProperty]
    private PlotModel _sectionPlot = new();

    [ObservableProperty]
    private string _title = "🧩 Aegis Policy Sections";

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
                CreateEmptyPlot("⚠️ Policy file not found");
                return;
            }

            var json = File.ReadAllText(policyPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("AegisPolicy", out var root))
            {
                CreateEmptyPlot("⚠️ Invalid policy structure");
                return;
            }

            var names = new List<string>();
            var counts = new List<int>();

            foreach (var prop in root.EnumerateObject())
            {
                names.Add(prop.Name);
                counts.Add(prop.Value.EnumerateObject().Count());
            }

           BuildSectionPlot(names, counts);
        }
        catch (Exception ex)
        {
            CreateEmptyPlot("❌ Failed to load policy data");
            Console.WriteLine(ex);
        }
    }

    // ---------------------------------------------------------------------
    // 🧱 Build OxyPlot model
    // ---------------------------------------------------------------------
    private PlotModel BuildSectionPlot(List<string> names, List<int> counts)
    {
        var model = new PlotModel
        {
            Title = "Policy Sections and Rule Counts",
            TextColor = OxyColors.White,
            Background = OxyColor.FromRgb(30, 30, 30),
            PlotAreaBorderColor = OxyColors.Gray
        };

        var catAxis = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            TextColor = OxyColors.White,
            Title = "Sections"
        };
        catAxis.Labels.AddRange(names);

        var valAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Rule Count",
            TextColor = OxyColors.White,
            MajorGridlineStyle = LineStyle.Solid
        };

        // BarSeries is always horizontal, so we’ll just flip axis order for vertical layout
        var barSeries = new BarSeries
        {
            Title = "Rules per Section",
            FillColor = OxyColor.FromRgb(0, 191, 255),
            StrokeColor = OxyColors.White,
            StrokeThickness = 1,
            LabelPlacement = LabelPlacement.Inside,
            LabelFormatString = "{0}",
            ItemsSource = counts.Select(c => new BarItem { Value = c }).ToList()
        };

        model.Axes.Add(catAxis);
        model.Axes.Add(valAxis);
        model.Series.Add(barSeries);

        return model;
    }

    // ---------------------------------------------------------------------
    // 🧩 Placeholder chart
    // ---------------------------------------------------------------------
    private PlotModel CreateEmptyPlot(string message) => new()
    {
        Title = message,
        TextColor = OxyColors.White,
        Background = OxyColor.FromRgb(30, 30, 30)
    };
}
