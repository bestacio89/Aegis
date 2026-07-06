using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
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
    private ObservableCollection<ISeries> _series = new();

    [ObservableProperty]
    private Axis[] _xAxes = [];

    [ObservableProperty]
    private Axis[] _yAxes = [];

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
        _ = LoadDataAsync();
    }

    public IAsyncRelayCommand LoadDataCommand { get; }

    private async Task LoadDataAsync()
    {
        try
        {
            _logger.LogInformation("Loading layer stats...");

            var reports = await _reportRepo.GetAllReportsAsync(default);
            var lastReport = reports.OrderByDescending(r => r.ScanDate).FirstOrDefault();

            if (lastReport is null)
            {
                BuildEmpty("No reports found");
                return;
            }

            var violations = await _ruleRepo.GetViolationsByReportIdAsync(lastReport.Id, default);

            if (violations is null || !violations.Any())
            {
                BuildEmpty("No violations found");
                return;
            }

            var grouped = violations
                .GroupBy(v =>
                {
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

            Layers = new ObservableCollection<LayerStat>(grouped);

            BuildChart(grouped);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to load layer dashboard");
            BuildEmpty("Error loading data");
        }
    }

    // ------------------------------------------------------------------
    // 📊 LiveCharts version of bar chart
    // ------------------------------------------------------------------
    private void BuildChart(IEnumerable<LayerStat> layers)
    {
        var data = layers.ToList();

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
                Labels = data.Select(x => x.Layer).ToArray(),
                LabelsRotation = 15
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

    // ------------------------------------------------------------------
    // 🧱 Empty state
    // ------------------------------------------------------------------
    private void BuildEmpty(string message)
    {
        Series = new ObservableCollection<ISeries>();

        XAxes = new[]
        {
            new Axis { Name = message }
        };

        YAxes = new[]
        {
            new Axis { Name = "" }
        };
    }
}

// ----------------------------------------------------------------------
// 🧩 Model stays unchanged
// ----------------------------------------------------------------------
public sealed record LayerStat(string Layer, int Count);