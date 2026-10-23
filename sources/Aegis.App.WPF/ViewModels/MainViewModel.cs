
using Aegis.Sdk;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;
using Aegis.Wpf.ViewModels.Configuration;
using Aegis.Wpf.ViewModels.Security;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Aegis.Wpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly AegisArchitectureAnalysisRunner _runner;

    private readonly ArchitectureOverviewViewModel _architectureOverview;
    private readonly SectionDashboardViewModel _sectionDashboard;
    private readonly LayerDashboardViewModel _layerDashboard;
    private readonly RuleDashboardViewModel _ruleDashboard;
    private readonly ReportVisualizationViewModel _reportVisualization;
    private readonly SecurityWorkspaceViewModel _securityWorkspace;
    private readonly ConfigurationWorkspaceViewModel _configurationWorkspace;
    private readonly ArchitectureWorkspaceViewModel _architectureWorkspace;

    private AegisArchitectureReport? _currentReport;
    private ProjectArchitectureContext? _currentContext;

    private string _repositoryPath = string.Empty;
    private double _analysisProgress;
    private object? _activeWorkspaceViewModel;
    private string? _selectedWorkspace;
    private string _selectedExportFormat = "pdf";

    public MainViewModel(
        AegisArchitectureAnalysisRunner runner,
        ArchitectureOverviewViewModel architectureOverview,
        SectionDashboardViewModel sectionDashboard,
        LayerDashboardViewModel layerDashboard,
        RuleDashboardViewModel ruleDashboard,
        ReportVisualizationViewModel reportVisualization,
        SecurityWorkspaceViewModel securityWorkspace,
        ConfigurationWorkspaceViewModel configurationWorkspace)
    {
        _runner = runner;

        _architectureOverview = architectureOverview;
        _sectionDashboard = sectionDashboard;
        _layerDashboard = layerDashboard;
        _ruleDashboard = ruleDashboard;
        _reportVisualization = reportVisualization;
        _securityWorkspace = securityWorkspace;
        _configurationWorkspace = configurationWorkspace;

        _architectureWorkspace = new ArchitectureWorkspaceViewModel(
            _architectureOverview,
            _sectionDashboard,
            _layerDashboard,
            _ruleDashboard,
            _reportVisualization);

        SelectRepositoryCommand =
            new RelayCommand(ExecuteSelectRepository);

        RunAnalysisCommand =
            new AsyncRelayCommand(ExecuteRunAnalysisAsync);

        ExportResultsCommand =
            new AsyncRelayCommand(ExecuteExportAsync);

        ClearLogsCommand =
            new RelayCommand(ClearLogs);

        ShowArchitectureWorkspaceCommand =
            new RelayCommand(ShowArchitectureWorkspace);

        ShowSecurityWorkspaceCommand =
            new RelayCommand(() =>
                ShowWorkspace(_securityWorkspace, "Security"));

        ShowConfigurationWorkspaceCommand =
            new RelayCommand(() =>
                ShowWorkspace(_configurationWorkspace, "Configuration"));

        // Retained for compatibility with existing bindings.
        ShowSectionWorkspaceCommand =
            new RelayCommand(() => ShowArchitectureTab("Sections"));

        ShowLayerWorkspaceCommand =
            new RelayCommand(() => ShowArchitectureTab("Layers"));

        ShowRuleWorkspaceCommand =
            new RelayCommand(() => ShowArchitectureTab("Rules"));

        ShowReportVisualizationCommand =
            new RelayCommand(() => ShowArchitectureTab("Reports"));

        foreach (var format in _runner.AvailableExportFormats)
        {
            ExportFormats.Add(format);
        }

        // Start in the Architecture workspace.
        ShowArchitectureWorkspace();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RepositoryPath
    {
        get => _repositoryPath;
        set => SetField(ref _repositoryPath, value);
    }

    public double AnalysisProgress
    {
        get => _analysisProgress;
        set => SetField(ref _analysisProgress, value);
    }

    public object? ActiveWorkspaceViewModel
    {
        get => _activeWorkspaceViewModel;
        set => SetField(ref _activeWorkspaceViewModel, value);
    }

    public string? SelectedWorkspace
    {
        get => _selectedWorkspace;
        set => SetField(ref _selectedWorkspace, value);
    }

    public string SelectedExportFormat
    {
        get => _selectedExportFormat;
        set => SetField(ref _selectedExportFormat, value);
    }

    public ObservableCollection<string> Logs { get; } = new();

    public ObservableCollection<string> ExportFormats { get; } = new();

    public ICommand ShowArchitectureWorkspaceCommand { get; }

    public ICommand ShowSecurityWorkspaceCommand { get; }

    public ICommand ShowConfigurationWorkspaceCommand { get; }

    public ICommand SelectRepositoryCommand { get; }

    public ICommand RunAnalysisCommand { get; }

    public ICommand ExportResultsCommand { get; }

    public ICommand ClearLogsCommand { get; }

    // Compatibility commands: these now open Architecture and select a tab.
    public ICommand ShowSectionWorkspaceCommand { get; }

    public ICommand ShowLayerWorkspaceCommand { get; }

    public ICommand ShowRuleWorkspaceCommand { get; }

    public ICommand ShowReportVisualizationCommand { get; }

    private void ShowArchitectureWorkspace()
    {
        ShowWorkspace(_architectureWorkspace, "Architecture");
    }

    private void ShowArchitectureTab(string tabName)
    {
        _architectureWorkspace.SelectTab(tabName);
        ShowArchitectureWorkspace();
    }

    private void ShowWorkspace(object workspace, string name)
    {
        ActiveWorkspaceViewModel = workspace;
        SelectedWorkspace = name;
    }

    private void ExecuteSelectRepository()
    {
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        var defaultRepos = Path.Combine(
            userProfile,
            "Source",
            "Repos");

        var dialog = new OpenFolderDialog
        {
            Title = "Select Architecture Repository Root",
            InitialDirectory = Directory.Exists(defaultRepos)
                ? defaultRepos
                : userProfile
        };

        if (dialog.ShowDialog() != true)
            return;

        RepositoryPath = dialog.FolderName;

        Logs.Add($"[INFO] Repository selected: {RepositoryPath}");
    }

    private async Task ExecuteRunAnalysisAsync()
    {
        if (string.IsNullOrWhiteSpace(RepositoryPath) ||
            !Directory.Exists(RepositoryPath))
        {
            Logs.Add("[WARN] Select an existing repository directory first.");
            return;
        }

        if (!await _syncLock.WaitAsync(0))
        {
            Logs.Add("[WARN] Analysis already running.");
            return;
        }

        try
        {
            Logs.Add("[EXEC] Starting Aegis architecture analysis...");

            AnalysisProgress = 0;

            var result = await _runner.RunSessionAsync(
                RepositoryPath,
                policyPath: null);

            if (!result.Success)
            {
                Logs.Add($"[ERROR] Analysis failed: {result.ErrorMessage}");
                return;
            }

            _currentReport = result.Report;
            _currentContext = result.Context;

            RefreshDashboards(_currentReport, _currentContext);

            AnalysisProgress = 100;

            Logs.Add("[SUCCESS] Analysis completed.");
        }
        catch (Exception ex)
        {
            Logs.Add($"[FATAL] {ex.Message}");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private void RefreshDashboards(
        AegisArchitectureReport report,
        ProjectArchitectureContext context)
    {
        _architectureOverview.Update(report, context);
        _layerDashboard.Update(report);
        _sectionDashboard.Update(report);
        _ruleDashboard.Update(report);
        _reportVisualization.Update(report, context);
    }

    private async Task ExecuteExportAsync()
    {
        if (_currentReport is null || _currentContext is null)
        {
            Logs.Add("[WARN] No report available.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Aegis Report",
            FileName =
                $"AegisReport_{_currentReport.ProjectName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}",
            Filter =
                $"{SelectedExportFormat.ToUpperInvariant()} file|*.{SelectedExportFormat}"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            Logs.Add($"[EXEC] Exporting report -> {dialog.FileName}");

            await _runner.ExportReportAsync(
                _currentReport,
                _currentContext,
                SelectedExportFormat,
                dialog.FileName);

            Logs.Add("[SUCCESS] Export completed.");
        }
        catch (Exception ex)
        {
            Logs.Add($"[EXPORT ERROR] {ex.Message}");
        }
    }

    private void ClearLogs()
    {
        Logs.Clear();
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;

        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));

        return true;
    }
}
