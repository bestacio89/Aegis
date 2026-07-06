using Aegis.App.Wpf.Services.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;

namespace Aegis.App.Wpf.ViewModels;

public sealed partial class LayerDashboardViewModel : ObservableObject
{
    private readonly ILayerAnalysisService _service;
    private readonly ILogger<LayerDashboardViewModel> _logger;

    public LayerDashboardViewModel(
        ILayerAnalysisService service,
        ILogger<LayerDashboardViewModel> logger)
    {
        _service = service;
        _logger = logger;

        LoadDataCommand = new AsyncRelayCommand(LoadAsync);

        _ = LoadAsync();
    }

    // =========================
    // COMMANDS
    // =========================
    public IAsyncRelayCommand LoadDataCommand { get; }

    // =========================
    // STATE
    // =========================
    [ObservableProperty]
    private ObservableCollection<ISeries> series = new();

    [ObservableProperty]
    private Axis[] xAxes = [];

    [ObservableProperty]
    private Axis[] yAxes = [];

    [ObservableProperty]
    private ObservableCollection<LayerStat> layers = new();

    // =========================
    // LOAD
    // =========================
    private async Task LoadAsync()
    {
        try
        {
            _logger.LogInformation("Loading layer dashboard...");

            var data = await _service.GetLayerStatsAsync(default);

            Layers = new ObservableCollection<LayerStat>(data);

            BuildChart(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Layer dashboard failed");
        }
    }

    // =========================
    // CHART ONLY (UI responsibility)
    // =========================
    private void BuildChart(IReadOnlyList<LayerStat> data)
    {
        Series = new ObservableCollection<ISeries>
        {
            new ColumnSeries<int>
            {
                Name = "Violations",
                Values = data.Select(x => x.Count).ToArray(),
                Fill = new SolidColorPaint(new SKColor(0, 191, 255))
            }
        };

        XAxes = new[]
        {
            new Axis
            {
                Labels = data.Select(x => x.Layer).ToArray()
            }
        };

        YAxes = new[]
        {
            new Axis
            {
                Name = "Violations"
            }
        };
    }
}