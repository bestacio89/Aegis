using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elastic.CommonSchema;
using Microsoft.Extensions.Logging;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class LayerDashboardViewModel : ObservableObject
{
    private readonly IRuleResultRepository _ruleRepo;
    private readonly IReportRepository _reportRepo;
    private readonly ILogger<LayerDashboardViewModel> _logger;

    [ObservableProperty]
    private PlotModel _layerPlot = new();

    [ObservableProperty]
    private ObservableCollection<LayerStat> _layers = new();

    public LayerDashboardViewModel(
        IRuleResultRepository ruleRepo,
        IReportRepository reportRepo,
        ILogger<LayerDashboardViewModel> logger)
    {
        _ruleRepo = ruleRepo;
        _reportRepo = reportRepo;
        _logger = logger;

        LoadDataCommand = new AsyncRelayCommand(LoadDataAsync);
        _ = LoadDataAsync(); // auto-load
    }

    public IAsyncRelayCommand LoadDataCommand { get; }

    private async Task LoadDataAsync()
    {
        try
        {
            _logger.LogInformation("📊 Loading layer stats...");

            var reports = await _reportRepo.GetAllReportsAsync(default);
            var lastReport = reports.OrderByDescending(r => r.ScanDate).FirstOrDefault();
            if (lastReport == null)
            {
                CreateEmptyPlot("⚠️ No reports found");
                return;
            }

            var violations = await _ruleRepo.GetViolationsByReportIdAsync(lastReport.Id, default);
            if (violations == null || !violations.Any())
            {
               CreateEmptyPlot("⚠️ No violations found");
                return;
            }

            // Group by "Layer" (assuming it's part of your model’s Target or Category)
            var grouped = violations
                .GroupBy(v =>
                {
                    // Derive layer name from Target, Category, or naming convention
                    var target = v.Target ?? string.Empty;
                    if (target.Contains("Api", System.StringComparison.OrdinalIgnoreCase)) return "API";
                    if (target.Contains("App", System.StringComparison.OrdinalIgnoreCase)) return "Application";
                    if (target.Contains("Domain", System.StringComparison.OrdinalIgnoreCase)) return "Domain";
                    if (target.Contains("Infrastructure", System.StringComparison.OrdinalIgnoreCase)) return "Infrastructure";
                    return "Other";
                })
                .Select(g => new LayerStat(g.Key, g.Count()))
                .OrderByDescending(x => x.Count)
                .ToList();

             new ObservableCollection<LayerStat>(grouped);
             BuildLayerPlot(grouped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to load layer dashboard");
             CreateEmptyPlot("❌ Error loading data");
        }
    }

    // ---------------------------------------------------------------------
    // 🧱 Build OxyPlot bar chart
    // ---------------------------------------------------------------------
    private PlotModel BuildLayerPlot(IEnumerable<LayerStat> layers)
    {
        var model = new PlotModel
        {
            Title = "Violations by Architecture Layer",
            TextColor = OxyColors.White,
            Background = OxyColor.FromRgb(30, 30, 30),
            PlotAreaBorderColor = OxyColors.Gray
        };

        var catAxis = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            TextColor = OxyColors.White,
            Title = "Layer"
        };
        catAxis.Labels.AddRange(layers.Select(l => l.Layer));

        var valAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Violation Count",
            TextColor = OxyColors.White,
            MajorGridlineStyle = LineStyle.Solid
        };

        var barSeries = new BarSeries
        {
            Title = "Violations per Layer",
            FillColor = OxyColor.FromRgb(0, 191, 255),
            StrokeColor = OxyColors.White,
            StrokeThickness = 1,
            LabelPlacement = LabelPlacement.Inside,
            LabelFormatString = "{0}",
            ItemsSource = layers.Select(l => new BarItem { Value = l.Count }).ToList()
        };

        model.Axes.Add(catAxis);
        model.Axes.Add(valAxis);
        model.Series.Add(barSeries);
        return model;
    }

    // ---------------------------------------------------------------------
    // 🧩 Placeholder chart
    // ---------------------------------------------------------------------
    private static PlotModel CreateEmptyPlot(string message) => new()
    {
        Title = message,
        TextColor = OxyColors.White,
        Background = OxyColor.FromRgb(30, 30, 30)
    };
}

// 🧩 Helper record for DataGrid
public sealed record LayerStat(string Layer, int Count);
