using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// Represents a navigable area of the architecture workspace and remembers
/// the last selected view within that area.
/// </summary>
public sealed class ArchitectureWorkspaceSection : INotifyPropertyChanged
{
    private ArchitectureTab? _selectedTab;

    public ArchitectureWorkspaceSection(
        string name,
        string glyph,
        string description,
        IEnumerable<ArchitectureTab> tabs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(glyph);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(tabs);

        var materializedTabs = tabs.ToArray();
        if (materializedTabs.Length == 0)
        {
            throw new ArgumentException(
                "An architecture workspace section must contain at least one view.",
                nameof(tabs));
        }

        if (materializedTabs.Any(tab => tab is null))
        {
            throw new ArgumentException(
                "Architecture workspace sections cannot contain null views.",
                nameof(tabs));
        }

        Name = name;
        Glyph = glyph;
        Description = description;
        Tabs = Array.AsReadOnly(materializedTabs);
        _selectedTab = materializedTabs[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Glyph { get; }

    public string Description { get; }

    public IReadOnlyList<ArchitectureTab> Tabs { get; }

    public ArchitectureTab? SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (ReferenceEquals(_selectedTab, value))
            {
                return;
            }

            _selectedTab = value;
            OnPropertyChanged();
        }
    }

    public bool SelectTab(ArchitectureTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (!Tabs.Any(candidate => ReferenceEquals(candidate, tab)))
        {
            return false;
        }

        SelectedTab = tab;
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
