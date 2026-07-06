using Aegis.Sdk;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Aegis.App.Wpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private readonly AegisArchitectureAnalysisRunner _runner;
    private readonly IReadOnlyCollection<IReportExporter> _exporters;

    private string _repositoryPath = string.Empty;
    private double _analysisProgress;
    private object? _activeWorkspaceViewModel;
    private string? _selectedWorkspace;
    private object? _selectedItem;

    private string _selectedExportFormat = "pdf";
    private AegisArchitectureReport? _currentReport;


    public event PropertyChangedEventHandler? PropertyChanged;


    public MainViewModel(
        AegisArchitectureAnalysisRunner runner,
        IEnumerable<IReportExporter> exporters)
    {
        _runner = runner;
        _exporters = exporters.ToList();


        SelectRepositoryCommand =
            new RelayCommand(_ => ExecuteSelectRepository());


        RunAnalysisCommand =
            new RelayCommand(
                async _ => await ExecuteRunAnalysisAsync(),
                _ => !string.IsNullOrWhiteSpace(RepositoryPath));


        ExportResultsCommand =
            new RelayCommand(
                async _ => await ExecuteExportAsync(),
                _ => _currentReport != null);


        ClearLogsCommand =
            new RelayCommand(_ => Logs.Clear());


        ShowSectionWorkspaceCommand =
            new RelayCommand(_ =>
            {
                SelectedWorkspace = "Sections";
            });


        ShowLayerWorkspaceCommand =
            new RelayCommand(_ =>
            {
                SelectedWorkspace = "Layers";
            });


        ShowRuleWorkspaceCommand =
            new RelayCommand(_ =>
            {
                SelectedWorkspace = "Rules";
            });


        foreach (var exporter in _exporters)
        {
            ExportFormats.Add(exporter.Format);
        }
    }



    public string RepositoryPath
    {
        get => _repositoryPath;
        set
        {
            if (SetField(ref _repositoryPath, value))
            {
                (RunAnalysisCommand as RelayCommand)
                    ?.RaiseCanExecuteChanged();
            }
        }
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


    public object? SelectedItem
    {
        get => _selectedItem;
        set => SetField(ref _selectedItem, value);
    }


    public string SelectedExportFormat
    {
        get => _selectedExportFormat;
        set => SetField(ref _selectedExportFormat, value);
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




    private void ExecuteSelectRepository()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Architecture Repository Root",
            InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
        };


        if (dialog.ShowDialog() == true)
        {
            RepositoryPath = dialog.FolderName;

            Logs.Add(
                $"[INFO] Target repository shifted to: {RepositoryPath}");
        }
    }



    private async Task ExecuteRunAnalysisAsync()
    {
        if (!await _syncLock.WaitAsync(0))
            return;


        try
        {
            Logs.Add(
                "[EXEC] Starting Aegis architecture analysis...");


            AnalysisProgress = 0;


            var task = Task.Run(async () =>
            {
                return await _runner.RunSessionAsync(
                    RepositoryPath,
                    policyPath: null,
                    exportJson: true);
            });


            while (!task.IsCompleted)
            {
                if (AnalysisProgress < 90)
                    AnalysisProgress += 2;


                await Task.Delay(150);
            }


            var result = await task;


            AnalysisProgress = 100;


            if (result.Success)
            {
                Logs.Add(
                    "[SUCCESS] Analysis completed successfully.");

                Logs.Add(
                    "[INFO] Exporters are now available.");
            }
            else
            {
                Logs.Add(
                    "[ERROR] Analysis failed.");
            }


            (ExportResultsCommand as RelayCommand)
                ?.RaiseCanExecuteChanged();
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



    private async Task ExecuteExportAsync()
    {
        if (_currentReport == null)
        {
            Logs.Add(
                "[WARN] No analysis report available.");

            return;
        }


        var exporter = _exporters
            .FirstOrDefault(x =>
                x.Format.Equals(
                    SelectedExportFormat,
                    StringComparison.OrdinalIgnoreCase));


        if (exporter == null)
        {
            Logs.Add(
                $"[ERROR] Exporter not found: {SelectedExportFormat}");

            return;
        }


        var dialog = new SaveFileDialog
        {
            Title = "Export Aegis Report",
            Filter =
                $"{exporter.Format.ToUpper()} file|*.{exporter.Format}"
        };


        if (dialog.ShowDialog() != true)
            return;


        try
        {
            Logs.Add(
                $"[EXEC] Exporting report as {exporter.Format}...");


            await exporter.ExportAsync(
                _currentReport,
                new(),
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
            return false;


        field = value;

        PropertyChanged?
            .Invoke(this,
                new PropertyChangedEventArgs(propertyName));


        return true;
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


    public bool CanExecute(object? parameter)
        => _canExecute?.Invoke(parameter) ?? true;


    public void Execute(object? parameter)
        => _execute(parameter);


    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?
            .Invoke(this, EventArgs.Empty);
}