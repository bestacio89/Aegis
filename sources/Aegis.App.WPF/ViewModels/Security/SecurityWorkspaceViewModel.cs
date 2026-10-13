using Aegis.Wpf.Services.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;

namespace Aegis.Wpf.ViewModels.Security;

public sealed record SecurityTab(string Name, string Glyph, object ViewModel);

/// <summary>
/// The Security mode: target selection, run/cancel, on-demand export and the tabbed result views.
/// Architecture and Security stay independent modules; this class shares only UI conventions.
/// </summary>
public sealed partial class SecurityWorkspaceViewModel : ObservableObject
{
    private readonly ISecurityScanService _scanService;
    private readonly ILogger<SecurityWorkspaceViewModel> _logger;
    private readonly SecurityTab _findingsTab;

    private CancellationTokenSource? _cts;
    private SecurityScanOutcome? _lastOutcome;
    private string _reportLabel = "project";

    public SecurityWorkspaceViewModel(
        ISecurityScanService scanService,
        SecurityOverviewViewModel overview,
        SecurityDomainsViewModel domains,
        SecurityFindingsViewModel findings,
        ILogger<SecurityWorkspaceViewModel> logger)
    {
        _scanService = scanService;
        _logger = logger;

        Overview = overview;
        Domains = domains;
        Findings = findings;

        var overviewTab = new SecurityTab("Overview", "\uE80F", overview);
        var domainsTab = new SecurityTab("Domains", "\uE81E", domains);
        _findingsTab = new SecurityTab("Findings", "\uE7BA", findings);

        Tabs = new ObservableCollection<SecurityTab> { overviewTab, domainsTab, _findingsTab };
        SelectedTab = overviewTab;

        domains.OpenDomainRequested = category =>
        {
            findings.ShowDomain(category);
            SelectedTab = _findingsTab;
        };

        ExportFormats = _scanService.AvailableExportFormats.ToList();
        SelectedExportFormat = ExportFormats.FirstOrDefault() ?? "md";
    }

    public SecurityOverviewViewModel Overview { get; }
    public SecurityDomainsViewModel Domains { get; }
    public SecurityFindingsViewModel Findings { get; }

    public ObservableCollection<SecurityTab> Tabs { get; }
    public IReadOnlyList<string> ExportFormats { get; }

    // ----------------------------------------------------------------- state

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunScanCommand))]
    private string targetPath = string.Empty;

    [ObservableProperty] private string scanEnvironment = "Default";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoResult))]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    private bool hasResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty] private string statusMessage = "Select a target and run a security scan.";
    [ObservableProperty] private string selectedExportFormat = "md";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveContent))]
    private SecurityTab? selectedTab;

    public bool NoResult => !HasResult;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public object? ActiveContent => SelectedTab?.ViewModel;

    // -------------------------------------------------------------- commands

    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "Select the project to assess" };
        if (dialog.ShowDialog() == true)
            TargetPath = dialog.FolderName;
    }

    [RelayCommand(CanExecute = nameof(CanRunScan))]
    private async Task RunScanAsync()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        await ExecuteScanAsync(_cts.Token);
    }

    private bool CanRunScan() => !IsBusy && !string.IsNullOrWhiteSpace(TargetPath);

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportReportAsync()
    {
        if (_lastOutcome is null)
            return;

        var format = SelectedExportFormat;

        var dialog = new SaveFileDialog
        {
            Title = "Export Aegis Security Report",
            FileName = $"AegisSecurityReport_{_reportLabel}_{DateTime.Now:yyyyMMdd_HHmmss}",
            Filter = $"{format.ToUpperInvariant()} file|*.{format}",
            DefaultExt = format,
            AddExtension = true
        };

        if (dialog.ShowDialog() != true)
            return;

        await ExportToFileAsync(format, dialog.FileName, CancellationToken.None);
    }

    private bool CanExport() => HasResult && !IsBusy;

    // ------------------------------------------------- API used by orchestrator

    public async Task ScanAsync(string path, CancellationToken ct)
    {
        TargetPath = path;
        await ExecuteScanAsync(ct);
    }

    public async Task ExportToFileAsync(string format, string outputPath, CancellationToken ct)
    {
        if (_lastOutcome is null)
        {
            ErrorMessage = "Run a scan before exporting.";
            return;
        }

        try
        {
            ErrorMessage = null;
            await _scanService.ExportAsync(_lastOutcome, format, outputPath, ct);
            StatusMessage = $"Report exported ({format}) -> {outputPath}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Security report export failed");
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------ core

    private async Task ExecuteScanAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(TargetPath) || !Directory.Exists(TargetPath))
        {
            ErrorMessage = "Select an existing folder to assess.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Running security probes and evaluators...";

        try
        {
            var outcome = await _scanService.RunAsync(TargetPath, ScanEnvironment, ct);

            if (outcome.Success && outcome.View is not null)
            {
                _lastOutcome = outcome;
                _reportLabel = ToFileLabel(TargetPath);

                Overview.Update(outcome.View);
                Domains.Update(outcome.View);
                Findings.Update(outcome.View);

                HasResult = true;
                SelectedTab = Tabs[0];
                StatusMessage =
                    $"Scan complete - {outcome.View.RiskLabel} risk, {outcome.View.ComplianceRate:0.#}% compliant, {outcome.View.Findings.Count} findings.";
            }
            else
            {
                ErrorMessage = outcome.Error ?? "The scan did not complete.";
                StatusMessage = "Scan failed.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string ToFileLabel(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
            return "project";

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }
}