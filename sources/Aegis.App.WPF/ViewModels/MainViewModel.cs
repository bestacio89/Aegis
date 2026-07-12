using Aegis.Sdk;
using Aegis.Shared.Architecture.Models;

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

    private readonly SectionDashboardViewModel _sectionDashboard;
    private readonly LayerDashboardViewModel _layerDashboard;
    private readonly RuleDashboardViewModel _ruleDashboard;
    private readonly ReportVisualizationViewModel _reportVisualization;


    private AegisArchitectureReport? _currentReport;
    private ProjectArchitectureContext? _currentContext;


    private string _repositoryPath = string.Empty;
    private double _analysisProgress;

    private object? _activeWorkspaceViewModel;
    private object? _selectedItem;

    private string? _selectedWorkspace;

    private string _selectedExportFormat = "pdf";



    public event PropertyChangedEventHandler? PropertyChanged;



    public MainViewModel(
        AegisArchitectureAnalysisRunner runner,
        SectionDashboardViewModel sectionDashboard,
        LayerDashboardViewModel layerDashboard,
        RuleDashboardViewModel ruleDashboard,
        ReportVisualizationViewModel reportVisualization)
    {
        _runner = runner;

        _sectionDashboard = sectionDashboard;
        _layerDashboard = layerDashboard;
        _ruleDashboard = ruleDashboard;
        _reportVisualization = reportVisualization;



        SelectRepositoryCommand =
            new RelayCommand(
                ExecuteSelectRepository);



        RunAnalysisCommand =
            new AsyncRelayCommand(
                ExecuteRunAnalysisAsync);



        ExportResultsCommand =
            new AsyncRelayCommand(
                ExecuteExportAsync);



        ClearLogsCommand =
            new RelayCommand(
                ClearLogs);



        ShowSectionWorkspaceCommand =
            new RelayCommand(
                () =>
                    ShowWorkspace(
                        _sectionDashboard,
                        "Sections"));



        ShowLayerWorkspaceCommand =
            new RelayCommand(
                () =>
                    ShowWorkspace(
                        _layerDashboard,
                        "Layers"));



        ShowRuleWorkspaceCommand =
            new RelayCommand(
                () =>
                    ShowWorkspace(
                        _ruleDashboard,
                        "Rules"));



        ShowReportVisualizationCommand =
            new RelayCommand(
                () =>
                    ShowWorkspace(
                        _reportVisualization,
                        "Report"));



        foreach (var format in _runner.AvailableExportFormats)
        {
            ExportFormats.Add(format);
        }
    }



    public string RepositoryPath
    {
        get => _repositoryPath;

        set =>
            SetField(
                ref _repositoryPath,
                value);
    }



    public double AnalysisProgress
    {
        get => _analysisProgress;

        set =>
            SetField(
                ref _analysisProgress,
                value);
    }



    public object? ActiveWorkspaceViewModel
    {
        get => _activeWorkspaceViewModel;

        set =>
            SetField(
                ref _activeWorkspaceViewModel,
                value);
    }



    public object? SelectedItem
    {
        get => _selectedItem;

        set =>
            SetField(
                ref _selectedItem,
                value);
    }



    public string? SelectedWorkspace
    {
        get => _selectedWorkspace;

        set =>
            SetField(
                ref _selectedWorkspace,
                value);
    }



    public string SelectedExportFormat
    {
        get => _selectedExportFormat;

        set =>
            SetField(
                ref _selectedExportFormat,
                value);
    }



    public ObservableCollection<string> Logs { get; } = new();

    public ObservableCollection<string> ExportFormats { get; } = new();



    public ICommand SelectRepositoryCommand { get; }

    public ICommand RunAnalysisCommand { get; }

    public ICommand ExportResultsCommand { get; }

    public ICommand ClearLogsCommand { get; }


    public ICommand ShowSectionWorkspaceCommand { get; }

    public ICommand ShowLayerWorkspaceCommand { get; }

    public ICommand ShowRuleWorkspaceCommand { get; }

    public ICommand ShowReportVisualizationCommand { get; }



    private void ExecuteSelectRepository()
    {
        var userProfile =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);


        var defaultRepos =
            Path.Combine(
                userProfile,
                "Source",
                "Repos");


        var dialog =
            new OpenFolderDialog
            {
                Title =
                    "Select Architecture Repository Root",

                InitialDirectory =
                    Directory.Exists(defaultRepos)
                        ? defaultRepos
                        : userProfile
            };


        if (dialog.ShowDialog() == true)
        {
            RepositoryPath =
                dialog.FolderName;


            Logs.Add(
                $"[INFO] Repository selected: {RepositoryPath}");
        }
    }



    private async Task ExecuteRunAnalysisAsync()
    {
        if (!await _syncLock.WaitAsync(0))
        {
            Logs.Add(
                "[WARN] Analysis already running.");

            return;
        }


        try
        {
            Logs.Add(
                "[EXEC] Starting Aegis architecture analysis...");


            AnalysisProgress = 0;


            var result =
                await _runner.RunSessionAsync(
                    RepositoryPath,
                    policyPath: null);



            if (!result.Success)
            {
                Logs.Add(
                    $"[ERROR] Analysis failed: {result.ErrorMessage}");

                return;
            }



            _currentReport =
                result.Report;


            _currentContext =
                result.Context;



            AnalysisProgress = 100;



            RefreshDashboards(
                _currentReport,
                _currentContext);



            Logs.Add(
                "[SUCCESS] Analysis completed.");
        }
        catch (Exception ex)
        {
            Logs.Add(
                $"[FATAL] {ex.Message}");
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
        _layerDashboard.Update(
            report);


        _sectionDashboard.Update(
            report);


        _ruleDashboard.Update(
            report);


        _reportVisualization.Update(
            report,
            context);
    }



    private async Task ExecuteExportAsync()
    {
        if (_currentReport is null ||
            _currentContext is null)
        {
            Logs.Add(
                "[WARN] No report available.");

            return;
        }



        var dialog =
            new SaveFileDialog
            {
                Title =
                    "Export Aegis Report",

                FileName =
                    $"AegisReport_{_currentReport.ProjectName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}",

                Filter =
                    $"{SelectedExportFormat.ToUpper()} file|*.{SelectedExportFormat}"
            };



        if (dialog.ShowDialog() != true)
        {
            return;
        }



        try
        {
            Logs.Add(
                $"[EXEC] Exporting report → {dialog.FileName}");



            await _runner.ExportReportAsync(
                _currentReport,
                _currentContext,
                SelectedExportFormat,
                dialog.FileName);



            Logs.Add(
                "[SUCCESS] Export completed.");
        }
        catch (Exception ex)
        {
            Logs.Add(
                $"[EXPORT ERROR] {ex.Message}");
        }
    }



    private void ClearLogs()
    {
        Logs.Clear();
    }



    private void ShowWorkspace(
        object workspace,
        string name)
    {
        ActiveWorkspaceViewModel = workspace;

        SelectedWorkspace = name;
    }



    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(
            field,
            value))
        {
            return false;
        }


        field = value;


        PropertyChanged?
            .Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));


        return true;
    }
}