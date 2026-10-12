using Aegis.Security.Pipeline;
using Aegis.Security.Reporting.Contracts;
using Aegis.Shared.Security.Models;
using Aegis.Wpf.Models.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Wpf.Services.Security;



public interface ISecurityScanService
{
    IReadOnlyList<string> AvailableExportFormats { get; }

    Task<SecurityScanOutcome> RunAsync(string projectPath, string environment, CancellationToken ct);

    /// <summary>On-demand export. Nothing is written to disk unless the user asks for it.</summary>
    Task ExportAsync(SecurityScanOutcome outcome, string format, string outputPath, CancellationToken ct);
}


