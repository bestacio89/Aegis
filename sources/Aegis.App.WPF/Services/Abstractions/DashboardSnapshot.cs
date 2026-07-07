using Aegis.App.Wpf.models;
using Aegis.App.Wpf.Models;
using Aegis.App.Wpf.ViewModels;

namespace Aegis.App.Wpf.Services.Abstractions;

public sealed class DashboardSnapshot
{
    public IReadOnlyList<LayerStat> Layers { get; init; } = [];

    public IReadOnlyList<RuleDashboardItem> Rules { get; init; } = [];

    public IReadOnlyList<SectionDashboardItem> Sections { get; init; } = [];

    public DashboardMetrics Metrics { get; init; } =
        new(
            string.Empty,
            0,
            0,
            0d,
            0d,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            DateTimeOffset.Now);
}