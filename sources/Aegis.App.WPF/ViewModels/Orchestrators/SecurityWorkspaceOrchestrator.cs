using Aegis.Wpf.ViewModels.Security;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Windows.Input;

namespace Aegis.Wpf.ViewModels.Orchestrators;

/// <summary>
/// Adapts the Security workspace to IWorkspaceOrchestrator so the shell can drive it exactly like
/// any other mode. ActiveWorkspaceViewModel is the active TAB content (chrome-less), because the
/// shell supplies its own toolbar and calls RunAnalysisAsync / ExportAsync.
/// To show the self-contained workspace (with its own toolbar) instead, display
/// SecurityWorkspaceViewModel directly.
/// </summary>
public sealed class SecurityWorkspaceOrchestrator : IWorkspaceOrchestrator, INotifyPropertyChanged
{
    private readonly SecurityWorkspaceViewModel _workspace;

    public SecurityWorkspaceOrchestrator(SecurityWorkspaceViewModel workspace)
    {
        _workspace = workspace;

        Tabs = workspace.Tabs
            .Select(t => new WorkspaceTabDescriptor(
                t.Name,
                t.Glyph,
                new RelayCommand(() => workspace.SelectedTab = t)))
            .ToList();

        SelectTabCommand = new RelayCommand<WorkspaceTabDescriptor>(d => d?.SelectCommand.Execute(null));

        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SecurityWorkspaceViewModel.ActiveContent))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveWorkspaceViewModel)));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ModeName => "Security";

    public IReadOnlyList<WorkspaceTabDescriptor> Tabs { get; }

    public object ActiveWorkspaceViewModel => _workspace.ActiveContent ?? _workspace.Overview;

    public ICommand SelectTabCommand { get; }

    public Task RunAnalysisAsync(string repositoryPath, CancellationToken token) =>
        _workspace.ScanAsync(repositoryPath, token);

    public Task ExportAsync(string format, string outputPath, CancellationToken token) =>
        _workspace.ExportToFileAsync(format, outputPath, token);
}