using Aegis.Wpf.Services.Configuration;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace Aegis.Wpf.ViewModels.Configuration;

public sealed class ConfigurationWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly ConfigurationDocumentService _documentService;
    private ConfigurationDocument? _selectedDocument;
    private string _statusMessage = string.Empty;
    private string _statusKind = "Info";

    public ConfigurationWorkspaceViewModel(ConfigurationDocumentService documentService)
    {
        _documentService = documentService;

        SaveAllCommand = new RelayCommand(SaveAll);
        ReloadAllCommand = new RelayCommand(ReloadAll);
        OpenConfigurationFolderCommand = new RelayCommand(OpenConfigurationFolder);

        LoadDocuments();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ConfigurationDocument> Documents { get; } = new();

    public ConfigurationDocument? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (ReferenceEquals(_selectedDocument, value))
                return;

            _selectedDocument = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSettingsCount));
            OnPropertyChanged(nameof(SelectedDocumentPath));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string StatusKind
    {
        get => _statusKind;
        private set => SetField(ref _statusKind, value);
    }

    public string ConfigurationDirectory => _documentService.ConfigurationDirectory;
    public int LoadedDocumentCount => Documents.Count(document => document.IsLoaded);
    public int TotalSettingsCount => Documents.Sum(document => document.Settings.Count);
    public int DirtyDocumentCount => Documents.Count(document => document.IsDirty);
    public int SelectedSettingsCount => SelectedDocument?.Settings.Count ?? 0;
    public string SelectedDocumentPath => SelectedDocument?.FilePath ?? ConfigurationDirectory;
    public bool HasUnsavedChanges => Documents.Any(document => document.IsDirty);

    public ICommand SaveAllCommand { get; }
    public ICommand ReloadAllCommand { get; }
    public ICommand OpenConfigurationFolderCommand { get; }

    private void LoadDocuments(string? selectFileName = null)
    {
        foreach (var document in Documents)
        {
            document.PropertyChanged -= OnDocumentPropertyChanged;
        }

        Documents.Clear();

        foreach (var document in _documentService.LoadDocuments())
        {
            document.PropertyChanged += OnDocumentPropertyChanged;
            Documents.Add(document);
        }

        SelectedDocument = Documents.FirstOrDefault(document =>
            string.Equals(document.FileName, selectFileName, StringComparison.OrdinalIgnoreCase))
            ?? Documents.FirstOrDefault(document => document.IsLoaded)
            ?? Documents.FirstOrDefault();

        NotifySummaryProperties();

        var brokenDocuments = Documents.Where(document => !document.IsLoaded).ToArray();
        if (brokenDocuments.Length > 0)
        {
            StatusKind = "Error";
            StatusMessage = $"{brokenDocuments.Length} policy file(s) could not be loaded. Select a file marked with an error to inspect its status.";
        }
        else
        {
            StatusKind = "Info";
            StatusMessage = $"Loaded {LoadedDocumentCount} policy files and {TotalSettingsCount} editable settings.";
        }
    }

    private void SaveAll()
    {
        if (!_documentService.ValidateAll(Documents, out var validationMessage))
        {
            StatusKind = "Error";
            StatusMessage = "Save blocked. Correct the invalid values first." + Environment.NewLine + validationMessage;
            return;
        }

        var dirtyDocuments = Documents.Where(document => document.IsDirty).ToArray();
        if (dirtyDocuments.Length == 0)
        {
            StatusKind = "Info";
            StatusMessage = "No unsaved policy changes.";
            return;
        }

        try
        {
            foreach (var document in dirtyDocuments)
            {
                _documentService.SaveDocument(document);
            }

            StatusKind = "Success";
            StatusMessage = $"Saved {dirtyDocuments.Length} policy file(s). The policy configuration providers monitor these files for changes; already-created analysis objects may need to be recreated before they use updated values.";
            NotifySummaryProperties();
        }
        catch (Exception ex)
        {
            StatusKind = "Error";
            StatusMessage = $"Configuration save failed: {ex.Message}";
            NotifySummaryProperties();
        }
    }

    private void ReloadAll()
    {
        if (HasUnsavedChanges)
        {
            var answer = MessageBox.Show(
                "Reload policy files and discard all unsaved edits?",
                "Discard configuration changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;
        }

        var selectedFile = SelectedDocument?.FileName;
        LoadDocuments(selectedFile);
    }

    private void OpenConfigurationFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ConfigurationDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusKind = "Error";
            StatusMessage = $"Could not open the configuration directory: {ex.Message}";
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfigurationDocument.IsDirty)
            or nameof(ConfigurationDocument.DirtySettingCount)
            or nameof(ConfigurationDocument.LoadError))
        {
            NotifySummaryProperties();
        }
    }

    private void NotifySummaryProperties()
    {
        OnPropertyChanged(nameof(LoadedDocumentCount));
        OnPropertyChanged(nameof(TotalSettingsCount));
        OnPropertyChanged(nameof(DirtyDocumentCount));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SelectedSettingsCount));
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
