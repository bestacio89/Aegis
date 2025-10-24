using Aegis.Sdk;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MvvmHelpers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Aegis.App.Wpf.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    private readonly AegisRunner _runner;
    private readonly ILogger<MainViewModel> _logger;

    public ObservableCollection<string> Logs { get; } = new();
    public ICommand AnalyzeCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ReloadCommand { get; }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public MainViewModel()
    {
        // ✅ Resolve dependencies through the DI container (App.Host configured in App.xaml.cs)
        _runner = App.Host.Services.GetRequiredService<AegisRunner>();
        _logger = App.Host.Services.GetRequiredService<ILogger<MainViewModel>>();

        // ✅ Setup commands
        AnalyzeCommand = new AsyncRelayCommand(RunAnalysisAsync);
        ExportCommand = new AsyncRelayCommand(ExportReportAsync);
        ReloadCommand = new RelayCommand(Logs.Clear);
    }

    // 🚀───────────────────────────────────────────────
    // ANALYSIS WORKFLOW
    // ────────────────────────────────────────────────
    private async Task RunAnalysisAsync()
    {
        try
        {
            AppendLog("🚀 Starting Aegis analysis...");

            var projectPath = Environment.CurrentDirectory;
            var policyPath = Path.Combine("config", "aegis.policy.json");

            Progress = 10;
            AppendLog($"📂 Target project: {projectPath}");

            var resultCode = await _runner.RunSessionAsync(
                projectPath: projectPath,
                policyPath: policyPath,
                exportJson: true,
                token: default
            );

            Progress = 90;
            if (resultCode == 0)
                AppendLog("✅ Analysis complete! No violations detected.");
            else if (resultCode == -1)
                AppendLog("❌ Analysis failed during execution. See logs for details.");
            else
                AppendLog($"⚠️ Analysis finished with code {resultCode} (see report for details).");

            Progress = 100;
        }
        catch (Exception ex)
        {
            AppendLog($"💥 Exception during analysis: {ex.Message}");
            _logger.LogError(ex, "Error during Aegis analysis execution");
        }
    }

    // 🧾───────────────────────────────────────────────
    // REPORT EXPORT (placeholder or future PDF)
    // ────────────────────────────────────────────────
    private async Task ExportReportAsync()
    {
        try
        {
            AppendLog("📦 Exporting report...");
            await Task.Delay(400); // simulate process for now
            AppendLog("📄 Report exported successfully (JSON or PDF depending on exporter settings).");
        }
        catch (Exception ex)
        {
            AppendLog($"❌ Export failed: {ex.Message}");
            _logger.LogError(ex, "Error during report export");
        }
    }

    // 🪶───────────────────────────────────────────────
    // LOGGING UTILITIES
    // ────────────────────────────────────────────────
    private void AppendLog(string message)
    {
        Application.Current.Dispatcher.Invoke(() => Logs.Add(message));
        _logger.LogInformation(message);
    }
}
