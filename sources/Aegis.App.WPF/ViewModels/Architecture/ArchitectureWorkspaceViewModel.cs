using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aegis.Wpf.ViewModels.Architecture;

public sealed class ArchitectureWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly SystemLandscapeViewModel _systemLandscape;
    private readonly ArchitectureOverviewViewModel _overview;
    private readonly DependencyGraphViewModel _dependencyGraph;
    private readonly RelationshipLedgerViewModel _relationshipLedger;
    private readonly UnprojectedEvidenceViewModel _unprojectedEvidence;
    private readonly SectionDashboardViewModel _sections;
    private readonly LayerDashboardViewModel _layers;
    private readonly RuleDashboardViewModel _rules;
    private readonly FindingsLedgerViewModel _findings;
    private readonly ReportVisualizationViewModel _reports;

    private ArchitectureWorkspaceSection? _selectedSection;
    private ArchitectureTab? _selectedTab;

    public ArchitectureWorkspaceViewModel(
        SystemLandscapeViewModel systemLandscape,
        ArchitectureOverviewViewModel overview,
        DependencyGraphViewModel dependencyGraph,
        RelationshipLedgerViewModel relationshipLedger,
        UnprojectedEvidenceViewModel unprojectedEvidence,
        SectionDashboardViewModel sections,
        LayerDashboardViewModel layers,
        RuleDashboardViewModel rules,
        FindingsLedgerViewModel findings,
        ReportVisualizationViewModel reports)
    {
        _systemLandscape = systemLandscape ?? throw new ArgumentNullException(nameof(systemLandscape));
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _dependencyGraph = dependencyGraph ?? throw new ArgumentNullException(nameof(dependencyGraph));
        _relationshipLedger = relationshipLedger ?? throw new ArgumentNullException(nameof(relationshipLedger));
        _unprojectedEvidence = unprojectedEvidence ?? throw new ArgumentNullException(nameof(unprojectedEvidence));
        _sections = sections ?? throw new ArgumentNullException(nameof(sections));
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _findings = findings ?? throw new ArgumentNullException(nameof(findings));
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));

        Sections =
        [
            new ArchitectureWorkspaceSection(
                "System",
                "⌂",
                "Explore the system landscape, its logical boundaries, implementation units, and external resources.",
                [new ArchitectureTab("System Landscape", "⌂", _systemLandscape)]),
            new ArchitectureWorkspaceSection(
                "Assessment",
                "◈",
                "Review the architecture assessment, health indicators, and the system's overall structural posture.",
                [new ArchitectureTab("Overview", "◈", _overview)]),
            new ArchitectureWorkspaceSection(
                "Dependencies",
                "⌘",
                "Investigate directed dependencies and inspect the evidence behind each discovered relationship.",
                [
                    new ArchitectureTab("Dependency Graph", "⌘", _dependencyGraph),
                    new ArchitectureTab("Relationship Ledger", "▤", _relationshipLedger),
                    new ArchitectureTab("Unprojected Evidence", "⌕", _unprojectedEvidence)
                ]),
            new ArchitectureWorkspaceSection(
                "Governance",
                "◎",
                "Evaluate architectural boundaries, layer conformance, rules, metrics, and detected findings.",
                [
                    new ArchitectureTab("Sections", "▦", _sections),
                    new ArchitectureTab("Layers", "◫", _layers),
                    new ArchitectureTab("Rules & Metrics", "◎", _rules),
                    new ArchitectureTab("Findings", "⚑", _findings)
                ]),
            new ArchitectureWorkspaceSection(
                "Reports",
                "▤",
                "Review and export the evidence-backed architecture assessment.",
                [new ArchitectureTab("Reports", "▤", _reports)])
        ];

        Tabs = new ObservableCollection<ArchitectureTab>(Sections.SelectMany(section => section.Tabs));
        SelectedSection = Sections[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ArchitectureWorkspaceSection> Sections { get; }

    /// <summary>
    /// Flat compatibility view of all navigable tabs.
    /// </summary>
    public ObservableCollection<ArchitectureTab> Tabs { get; }

    public ArchitectureWorkspaceSection? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (ReferenceEquals(_selectedSection, value))
            {
                return;
            }

            if (value is not null && !Sections.Contains(value))
            {
                return;
            }

            _selectedSection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SectionDescription));
            OnPropertyChanged(nameof(SectionViewCount));

            SelectedTab = value?.SelectedTab;
            OnPropertyChanged(nameof(ActiveContent));
        }
    }

    public ArchitectureTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value))
            {
                return;
            }

            if (value is not null &&
                (_selectedSection is null ||
                 !_selectedSection.Tabs.Any(tab => ReferenceEquals(tab, value))))
            {
                return;
            }

            _selectedTab = value;

            if (value is not null)
            {
                _selectedSection?.SelectTab(value);
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveContent));
        }
    }

    public object? ActiveContent => SelectedTab?.ViewModel;

    public string SectionDescription =>
        SelectedSection?.Description ?? "Select an architecture work area.";

    public int SectionViewCount => SelectedSection?.Tabs.Count ?? 0;

    /// <summary>
    /// Selects a view by its historical name. Unknown names are ignored so
    /// compatibility commands cannot accidentally change the active view.
    /// </summary>
    public void SelectTab(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var normalizedName = name.Trim();
        if (normalizedName.Equals("Rules", StringComparison.OrdinalIgnoreCase) ||
            normalizedName.Equals("Rules & Findings", StringComparison.OrdinalIgnoreCase))
        {
            normalizedName = "Rules & Metrics";
        }

        foreach (var section in Sections)
        {
            var tab = section.Tabs.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

            if (tab is null)
            {
                continue;
            }

            SelectedSection = section;
            SelectedTab = tab;
            return;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
