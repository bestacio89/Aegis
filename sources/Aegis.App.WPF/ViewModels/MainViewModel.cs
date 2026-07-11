using Aegis.App.Wpf.models;
using Aegis.App.Wpf.Models;
using Aegis.App.Wpf.Services;
using Aegis.Architecture.RuleEngines;
using Aegis.Sdk;
using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Aegis.App.Wpf.ViewModels;

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
        ReportVisualizationViewModel visualizationViewModel)
    {
        _runner = runner;

        _sectionDashboard = sectionDashboard;
        _layerDashboard = layerDashboard;
        _ruleDashboard = ruleDashboard;
        _reportVisualization = visualizationViewModel;


        SelectRepositoryCommand =
            new RelayCommand(
                _ => ExecuteSelectRepository());


        RunAnalysisCommand =
            new RelayCommand(
                async _ => await ExecuteRunAnalysisAsync(),
                _ => !string.IsNullOrWhiteSpace(RepositoryPath));


        ExportResultsCommand =
            new RelayCommand(
                async _ => await ExecuteExportAsync(),
                _ => _currentReport != null);


        ClearLogsCommand =
            new RelayCommand(
                _ => Logs.Clear());


        ShowSectionWorkspaceCommand =
            new RelayCommand(
                _ => ShowWorkspace(
                    _sectionDashboard,
                    "Sections"));


        ShowLayerWorkspaceCommand =
            new RelayCommand(
                _ => ShowWorkspace(
                    _layerDashboard,
                    "Layers"));


        ShowRuleWorkspaceCommand =
            new RelayCommand(
                _ => ShowWorkspace(
                    _ruleDashboard,
                    "Rules"));


        ShowReportVisualizationCommand =
            new RelayCommand(
                _ => ShowWorkspace(
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

        set
        {
            if (SetField(
                    ref _repositoryPath,
                    value))
            {
                RaiseCanExecuteChanged(
                    RunAnalysisCommand);
            }
        }
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



    public ObservableCollection<string> Workspaces { get; } = new();


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



    private void ShowWorkspace(
        object workspace,
        string name)
    {
        ActiveWorkspaceViewModel = workspace;

        SelectedWorkspace = name;
    }



    private void ExecuteSelectRepository()
    {
        // Common developer paths for quick access
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string defaultRepos = Path.Combine(userProfile, "Source", "Repos");

        var dialog = new OpenFolderDialog
        {
            Title = "Select Architecture Repository Root",
            // Setting to a sensible default or null to let the OS decide (usually Documents/Home)
            InitialDirectory = Directory.Exists(defaultRepos) ? defaultRepos : userProfile
        };

        if (dialog.ShowDialog() == true)
        {
            RepositoryPath = dialog.FolderName;

            Logs.Add($"[INFO] Target repository shifted to: {RepositoryPath}");
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


            var analysisTask =
                Task.Run(
                    () =>
                        _runner.RunSessionAsync(
                            RepositoryPath,
                            policyPath: null));


            while (!analysisTask.IsCompleted)
            {
                if (AnalysisProgress < 90)
                {
                    AnalysisProgress += 2;
                }


                await Task.Delay(150);
            }


            var result =
                await analysisTask;


            AnalysisProgress = 100;


            if (!result.Success)
            {
                Logs.Add(
                    $"[ERROR] Analysis failed: {result.ErrorMessage ?? "unknown error"}");

                return;
            }


            _currentReport = result.Report;

            _currentContext = result.Context;


            RefreshDashboards(
                result.Report,
                result.Context);


            Logs.Add(
                "[SUCCESS] Analysis completed successfully.");

            Logs.Add(
                "[INFO] Dashboards refreshed.");

            Logs.Add(
                "[INFO] Export is now available.");


            RaiseCanExecuteChanged(
                ExportResultsCommand);
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
        // Left-join every known domain against actual violations, so a fully compliant layer
        // (e.g. Security with zero findings) still shows up with Count = 0 instead of being
        // absent from the chart entirely — absence previously read as "not evaluated" rather
        // than "evaluated and clean."
        var violationsByDomain =
            report.Results
                .GroupBy(x =>
                    string.IsNullOrWhiteSpace(x.Domain)
                        ? "Unknown"
                        : x.Domain)
                .ToDictionary(g => g.Key, g => g.Count());

        var layers =
            RuleEngine.KnownDomains
                .Select(domain =>
                    new LayerStat(
                        domain,
                        violationsByDomain.TryGetValue(domain, out var count) ? count : 0))
                .OrderByDescending(x => x.Count)
                .ToList();


        _layerDashboard.Update(
            layers);



        // Build sections directly from report.ComplianceScores rather than re-deriving categories
        // from report.Results: ComplianceScores already covers every category with a registered
        // rule (via ArchitectureRuleRegistry), scoring clean categories at 100 instead of omitting
        // them, whereas grouping report.Results directly only ever sees categories with violations.
        var violationsByCategory =
            report.Results
                .GroupBy(x => x.Category)
                .ToDictionary(g => g.Key, g => g.Count());

        _sectionDashboard.Update(
            report.ComplianceScores
                .Select(kvp =>
                {
                    var ruleCount = violationsByCategory.TryGetValue(kvp.Key, out var c) ? c : 0;
                    return new SectionDashboardItem(
                        kvp.Key.ToString(),
                        kvp.Key,
                        kvp.Value / 100.0,
                        ruleCount,
                        kvp.Value >= 80
                            ? "Compliant"
                            : "Non-Compliant",
                        ruleCount == 0
                            ? "No findings"
                            : $"{ruleCount} findings");
                })
                .ToList());



        _ruleDashboard.Update(
            report.Results
                .Select(x =>
                    new RuleDashboardItem(
                        x.RuleName,
                        x.Category,
                        x.Severity,
                        Path.GetFileName(x.FilePath),
                        x.Message,
                        x.WeightedImpact))
                .ToList());



        // Was never wired up at all: SummaryItems/ExecutiveMessage/HealthStatus sat on their
        // hardcoded defaults forever since nothing ever called Update on this view model.
        _reportVisualization.Update(
            report,
            context);
    }



    private async Task ExecuteExportAsync()
    {
        if (_currentReport == null ||
            _currentContext == null)
        {
            Logs.Add(
                "[WARN] No analysis report available.");

            return;
        }



        if (!_runner.AvailableExportFormats.Contains(
                SelectedExportFormat,
                StringComparer.OrdinalIgnoreCase))
        {
            Logs.Add(
                $"[ERROR] Exporter not found: {SelectedExportFormat}");

            return;
        }



        var dialog =
            new SaveFileDialog
            {
                Title = "Export Aegis Report",

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
                $"[EXEC] Exporting report as {SelectedExportFormat} → {dialog.FileName}...");


            await _runner.ExportReportAsync(
                _currentReport,
                _currentContext,
                SelectedExportFormat,
                dialog.FileName);


            Logs.Add(
                $"[SUCCESS] Export completed: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Logs.Add(
                $"[EXPORT ERROR] {ex.Message}");
        }
    }



    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
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



    private static void RaiseCanExecuteChanged(
        ICommand command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }
}



public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;

    private readonly Predicate<object?>? _canExecute;



    public event EventHandler? CanExecuteChanged;



    public RelayCommand(
        Action<object?> execute,
        Predicate<object?>? canExecute = null)
    {
        _execute = execute;

        _canExecute = canExecute;
    }



    public bool CanExecute(
        object? parameter)
        => _canExecute?.Invoke(parameter) ?? true;



    public void Execute(
        object? parameter)
        => _execute(parameter);



    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?
            .Invoke(
                this,
                EventArgs.Empty);
    }
}