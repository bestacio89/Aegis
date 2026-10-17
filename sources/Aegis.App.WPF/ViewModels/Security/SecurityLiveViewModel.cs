using Aegis.Security.Live;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Live;
using Aegis.Wpf.Services.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Collections.ObjectModel;

namespace Aegis.Wpf.ViewModels.Security;

/// <summary>
/// The Live probing tab: record the authorized scope, run passive-first probes against it,
/// review what was observed, and export the evidence. Static and live results are never merged here.
/// </summary>
public sealed partial class SecurityLiveViewModel : ObservableObject
{
    private readonly ILiveScanService _service;
    private readonly ILogger<SecurityLiveViewModel> _logger;

    private CancellationTokenSource? _cts;
    private LiveScanResult? _lastResult;
    private DateTimeOffset? _consentGivenAtUtc;
    private long _lastLogSequence;

    public SecurityLiveViewModel(ILiveScanService service, ILogger<SecurityLiveViewModel> logger)
    {
        _service = service;
        _logger = logger;

        Revalidate();
    }

    public ObservableCollection<string> ScopeIssues { get; } = new();
    public ObservableCollection<LiveProbeResult> Results { get; } = new();
    public ObservableCollection<ProbeLogEntry> LogEntries { get; } = new();

    public IReadOnlyList<ProbeIntensity> IntensityOptions { get; } = Enum.GetValues<ProbeIntensity>();

    // ------------------------------------------------------------ scope inputs

    [ObservableProperty] private string targetsText = string.Empty;
    [ObservableProperty] private string authorizationReference = string.Empty;
    [ObservableProperty] private string authorizedBy = string.Empty;
    [ObservableProperty] private DateTime? validFrom = DateTime.Today;
    [ObservableProperty] private DateTime? validUntil = DateTime.Today;
    [ObservableProperty] private ProbeIntensity maxIntensity = ProbeIntensity.Passive;
    [ObservableProperty] private bool consentAcknowledged;

    partial void OnTargetsTextChanged(string value) => Revalidate();
    partial void OnAuthorizationReferenceChanged(string value) => Revalidate();
    partial void OnAuthorizedByChanged(string value) => Revalidate();
    partial void OnValidFromChanged(DateTime? value) => Revalidate();
    partial void OnValidUntilChanged(DateTime? value) => Revalidate();
    partial void OnMaxIntensityChanged(ProbeIntensity value) => Revalidate();

    partial void OnConsentAcknowledgedChanged(bool value)
    {
        // The moment of confirmation is part of the evidence, so capture it when the box is ticked.
        _consentGivenAtUtc = value ? DateTimeOffset.UtcNow : null;
        Revalidate();
    }

    // ------------------------------------------------------------------ state

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunLiveCommand))]
    private bool isScopeValid;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunLiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelLiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportEvidenceCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportEvidenceCommand))]
    private bool hasRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty] private string statusMessage = "Define the authorized scope to enable live probing.";
    [ObservableProperty] private string resultsSummary = "No live observations yet.";
    [ObservableProperty] private LiveProbeResult? selectedResult;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasScopeIssues => ScopeIssues.Count > 0;

    // --------------------------------------------------------------- commands

    [RelayCommand(CanExecute = nameof(CanRunLive))]
    private async Task RunLiveAsync()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        var scope = BuildScope();

        Results.Clear();
        LogEntries.Clear();
        SelectedResult = null;
        _lastLogSequence = 0;
        ErrorMessage = null;
        IsBusy = true;
        StatusMessage = "Live probing in progress...";

        var progress = new Progress<ProbeLogEntry>(AppendLog);

        try
        {
            var result = await _service.RunAsync(scope, progress, _cts.Token);

            _lastResult = result;
            HasRun = true;

            // Progress callbacks are posted asynchronously; make sure nothing is missing, in order.
            foreach (var entry in result.Log)
                AppendLog(entry);

            foreach (var r in result.Results.OrderByDescending(r => r.Severity).ThenBy(r => r.Target))
                Results.Add(r);

            var serious = result.Results.Count(r => r.Severity >= SecuritySeverity.High);
            ResultsSummary = result.Results.Count == 0
                ? "No observations were recorded."
                : $"{result.Results.Count} observation(s); {serious} High or Critical; " +
                  $"log integrity {(result.LogIntegrityVerified ? "verified" : "NOT verified")}.";

            if (result.Blocked.Count > 0)
                ErrorMessage = string.Join("  |  ", result.Blocked);

            StatusMessage = result.Completed
                ? "Live probing complete."
                : result.Results.Count == 0 && result.Blocked.Count > 0
                    ? "Live probing was refused. See the reasons above and the probe log."
                    : "Live probing stopped before completing.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Live probing cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live probing failed");
            ErrorMessage = ex.Message;
            StatusMessage = "Live probing failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRunLive() => !IsBusy && IsScopeValid;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelLive() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanExportEvidence))]
    private async Task ExportEvidenceAsync()
    {
        if (_lastResult is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export live-probing evidence",
            FileName = $"AegisLiveEvidence_{DateTime.Now:yyyyMMdd_HHmmss}",
            Filter = "JSON file|*.json",
            DefaultExt = "json",
            AddExtension = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            ErrorMessage = null;
            await _service.ExportEvidenceAsync(_lastResult, dialog.FileName, CancellationToken.None);
            StatusMessage = $"Evidence exported -> {dialog.FileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live evidence export failed");
            ErrorMessage = $"Export failed: {ex.Message}";
        }
    }

    private bool CanExportEvidence() => HasRun && !IsBusy;

    // ---------------------------------------------------------------- helpers

    private void AppendLog(ProbeLogEntry entry)
    {
        if (entry.Sequence <= _lastLogSequence)
            return;

        _lastLogSequence = entry.Sequence;
        LogEntries.Add(entry);
    }

    private void Revalidate()
    {
        ScopeIssues.Clear();

        var (_, parseErrors) = LiveTargetParser.Parse(TargetsText);
        foreach (var error in parseErrors)
            ScopeIssues.Add(error);

        foreach (var issue in _service.Validate(BuildScope()).Issues)
            ScopeIssues.Add(issue);

        IsScopeValid = ScopeIssues.Count == 0;
        OnPropertyChanged(nameof(HasScopeIssues));
    }

    private LiveScanScope BuildScope()
    {
        var (targets, _) = LiveTargetParser.Parse(TargetsText);

        var from = (ValidFrom ?? DateTime.Today).Date;
        var until = (ValidUntil ?? DateTime.Today).Date.AddDays(1).AddTicks(-1);

        return new LiveScanScope
        {
            Targets = targets,
            ValidFromUtc = new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Local)).ToUniversalTime(),
            ValidUntilUtc = new DateTimeOffset(DateTime.SpecifyKind(until, DateTimeKind.Local)).ToUniversalTime(),
            MaxIntensity = MaxIntensity,
            AuthorizationReference = AuthorizationReference?.Trim() ?? string.Empty,
            AuthorizedBy = AuthorizedBy?.Trim() ?? string.Empty,
            ConsentGivenAtUtc = _consentGivenAtUtc
        };
    }
}