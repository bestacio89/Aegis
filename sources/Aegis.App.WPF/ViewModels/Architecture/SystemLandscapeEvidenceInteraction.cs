using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace Aegis.Wpf.ViewModels.Architecture;

public sealed partial class SystemLandscapeViewModel
{
    private readonly SystemLandscapeEvidenceResolver _evidenceResolver = new();

    public ObservableCollection<SystemLandscapeEvidenceItemViewModel> SelectedEvidenceItems { get; } = [];

    [ObservableProperty]
    private SystemLandscapeEvidenceItemViewModel? selectedEvidenceItem;

    [ObservableProperty]
    private string evidenceInspectionStatus = "Select a relationship to inspect its source evidence.";

    private void RefreshEvidenceItems(SystemLandscapeRelationshipViewModel? relationship)
    {
        SelectedEvidenceItems.Clear();
        SelectedEvidenceItem = null;

        if (relationship is null)
        {
            EvidenceInspectionStatus = "Select a relationship to inspect its source evidence.";
            OpenEvidenceSourceCommand.NotifyCanExecuteChanged();
            return;
        }

        if (_context is null)
        {
            EvidenceInspectionStatus = "No assessment snapshot is available.";
            OpenEvidenceSourceCommand.NotifyCanExecuteChanged();
            return;
        }

        foreach (var item in _evidenceResolver.Resolve(_context, relationship.Evidence))
            SelectedEvidenceItems.Add(item);

        EvidenceInspectionStatus = SelectedEvidenceItems.Count == 0
            ? "This relationship has no directly addressable evidence occurrences."
            : $"{SelectedEvidenceItems.Count} exact evidence occurrence(s) resolved against the current assessment snapshot.";
        OpenEvidenceSourceCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenEvidenceSource))]
    private void OpenEvidenceSource(SystemLandscapeEvidenceItemViewModel? item)
    {
        if (item?.VerifiedSourcePath is not { } path
            || !SystemLandscapeEvidenceResolver.TryResolveSourcePath(_context?.RootPath, path, out var verifiedPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = verifiedPath, UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            EvidenceInspectionStatus = $"Could not open the verified source file: {exception.Message}";
        }
    }

    private bool CanOpenEvidenceSource(SystemLandscapeEvidenceItemViewModel? item) =>
        item?.HasSourceLocation == true
        && SystemLandscapeEvidenceResolver.TryResolveSourcePath(_context?.RootPath, item.VerifiedSourcePath, out _);
}
