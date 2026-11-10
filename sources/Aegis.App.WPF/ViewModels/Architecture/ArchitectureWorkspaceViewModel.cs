using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aegis.Wpf.ViewModels.Architecture;

public sealed record ArchitectureTab(string Name, string Glyph, object ViewModel);

public sealed class ArchitectureWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly ArchitectureOverviewViewModel _overview;
    private readonly SectionDashboardViewModel _sections;
    private readonly LayerDashboardViewModel _layers;
    private readonly RuleDashboardViewModel _rules;
    private readonly FindingsLedgerViewModel _findings;
    private readonly ReportVisualizationViewModel _reports;
    private ArchitectureTab? _selectedTab;

    public ArchitectureWorkspaceViewModel(
        ArchitectureOverviewViewModel overview,
        SectionDashboardViewModel sections,
        LayerDashboardViewModel layers,
        RuleDashboardViewModel rules,
        FindingsLedgerViewModel findings,
        ReportVisualizationViewModel reports)
    {
        _overview = overview;
        _sections = sections;
        _layers = layers;
        _rules = rules;
        _findings = findings;
        _reports = reports;

        Tabs =
        [
            new ArchitectureTab("Overview", "◈", _overview),
            new ArchitectureTab("Sections", "▦", _sections),
            new ArchitectureTab("Layers", "◫", _layers),
            new ArchitectureTab("Rules & Metrics", "◎", _rules),
            new ArchitectureTab("Findings", "⚑", _findings),
            new ArchitectureTab("Reports", "▤", _reports)
        ];

        SelectedTab = Tabs[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ArchitectureTab> Tabs { get; }

    public ArchitectureTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value))
                return;

            _selectedTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveContent));
        }
    }

    public object? ActiveContent => SelectedTab?.ViewModel;

    public void SelectTab(string name)
    {
        var tab = Tabs.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
            || (name.Equals("Rules", StringComparison.OrdinalIgnoreCase)
                && candidate.Name.Equals("Rules & Metrics", StringComparison.OrdinalIgnoreCase))
            || (name.Equals("Rules & Findings", StringComparison.OrdinalIgnoreCase)
                && candidate.Name.Equals("Rules & Metrics", StringComparison.OrdinalIgnoreCase)));

        if (tab is not null)
            SelectedTab = tab;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
