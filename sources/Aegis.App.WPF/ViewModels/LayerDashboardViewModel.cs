using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class LayerDashboardViewModel : ObservableObject
{
    private readonly ILogger<LayerDashboardViewModel> _logger;

    public LayerDashboardViewModel(
        ILogger<LayerDashboardViewModel> logger)
    {
        _logger = logger;

        Layers = new ObservableCollection<LayerStat>();
        Series = new ObservableCollection<ISeries>();
    }

    // ============================================================
    // STATE
    // ============================================================

    [ObservableProperty]
    private ObservableCollection<LayerStat> layers;

    [ObservableProperty]
    private ObservableCollection<ISeries> series;

    [ObservableProperty]
    private Axis[] xAxes = [];

    [ObservableProperty]
    private Axis[] yAxes = [];

    // ============================================================
    // KPI
    // ============================================================

    [ObservableProperty]
    private int totalLayers;

    [ObservableProperty]
    private int totalViolations;

    [ObservableProperty]
    private string dominantLayer = "-";

    [ObservableProperty]
    private double averageViolationsPerLayer;

    // ============================================================
    // PUBLIC UPDATE
    // ============================================================

    public void Update(
        IReadOnlyCollection<LayerStat> layerStats)
    {
        try
        {
            Layers.Clear();

            foreach (var layer in layerStats.OrderByDescending(x => x.Count))
                Layers.Add(layer);

            totalLayers = Layers.Count;

            totalViolations = Layers.Sum(x => x.Count);

            dominantLayer =
                Layers
                    .OrderByDescending(x => x.Count)
                    .FirstOrDefault()?.Layer
                ?? "-";

            averageViolationsPerLayer =
                totalLayers == 0
                    ? 0
                    : (double)totalViolations / totalLayers;

            BuildChart(Layers);

            _logger.LogInformation(
                "Layer dashboard updated ({Count} layers).",
                totalLayers);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update layer dashboard.");
        }
    }

    // ============================================================
    // CHART
    // ============================================================

    private void BuildChart(
        IEnumerable<LayerStat> data)
    {
        var ordered =
            data.OrderByDescending(x => x.Count)
                .ToList();

        if (ordered.Count == 0)
        {
            Series.Clear();

            XAxes = [];

            YAxes = [];

            return;
        }

        Series.Clear();

        Series.Add(
            new ColumnSeries<int>
            {
                Name = "Violations",
                Values = ordered
                    .Select(x => x.Count)
                    .ToArray(),

                Fill = new SolidColorPaint(SKColors.DeepSkyBlue)
            });

        XAxes =
        [
            new Axis
            {
                Labels = ordered
                    .Select(x => x.Layer)
                    .ToArray(),

                LabelsRotation = 15
            }
        ];

        YAxes =
        [
            new Axis
            {
                Name = "Violations"
            }
        ];
    }
}

public sealed record LayerStat(
    string Layer,
    int Count);