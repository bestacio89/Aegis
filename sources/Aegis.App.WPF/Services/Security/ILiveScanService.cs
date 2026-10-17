using Aegis.Shared.Security.Live;

namespace Aegis.Wpf.Services.Security;

public interface ILiveScanService
{
    /// <summary>Checks a scope against the authorization gate without touching any target.</summary>
    ScopeValidationResult Validate(LiveScanScope scope);

    Task<LiveScanResult> RunAsync(LiveScanScope scope, IProgress<ProbeLogEntry> progress, CancellationToken ct);

    /// <summary>Writes the scope, observations and hash-chained probe log as a JSON evidence file.</summary>
    Task ExportEvidenceAsync(LiveScanResult result, string outputPath, CancellationToken ct);
}