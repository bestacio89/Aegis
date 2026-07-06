using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;

namespace Aegis.App.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;

    public MainViewModel(IServiceProvider services)
    {
        _services = services;

        Logs = new ObservableCollection<string>();

        Workspaces = new ObservableCollection<string>
        {
            "Layers",
            "Sections",
            "Rules"
        };

        SelectedWorkspace = "Layers";
        ShowLayerWorkspace();
    }

    // =========================
    // STATE
    // =========================

    [ObservableProperty]
    private string? repositoryPath;

    [ObservableProperty]
    private double analysisProgress;

    [ObservableProperty]
    private object? activeWorkspaceViewModel;

    [ObservableProperty]
    private string? selectedWorkspace;

    // FIX: was object, now minimal contract
    [ObservableProperty]
    private InspectableItemViewModel? selectedItem;

    public ObservableCollection<string> Logs { get; }

    // FIX: XAML was binding to Sections (did not exist)
    public ObservableCollection<string> Workspaces { get; }

    // alias to avoid rewriting XAML further if you want
    public ObservableCollection<string> Sections => Workspaces;

    // =========================
    // COMMANDS
    // =========================

    [RelayCommand]
    private void SelectRepository()
    {
        RepositoryPath = @"C:\repo";
        AddLog("Repository selected");
    }

    [RelayCommand]
    private void RunAnalysis()
    {
        AddLog("Analysis started");

        AnalysisProgress = 0;
        AnalysisProgress = 100;

        AddLog("Analysis completed");
    }

    [RelayCommand]
    private void ExportResults()
    {
        AddLog("Export started");
        AddLog("Export completed");
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
    }

    [RelayCommand]
    private void ShowLayerWorkspace()
    {
        ActiveWorkspaceViewModel =
            _services.GetRequiredService<LayerDashboardViewModel>();

        SelectedWorkspace = "Layers";
        AddLog("Switched to Layer workspace");
    }

    [RelayCommand]
    private void ShowSectionWorkspace()
    {
        ActiveWorkspaceViewModel =
            _services.GetRequiredService<SectionDashboardViewModel>();

        SelectedWorkspace = "Sections";
        AddLog("Switched to Section workspace");
    }

    [RelayCommand]
    private void ShowRuleWorkspace()
    {
        ActiveWorkspaceViewModel =
            _services.GetRequiredService<RuleDashboardViewModel>();

        SelectedWorkspace = "Rules";
        AddLog("Switched to Rule workspace");
    }

    // =========================
    // LOGGING
    // =========================

    private void AddLog(string message)
    {
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}