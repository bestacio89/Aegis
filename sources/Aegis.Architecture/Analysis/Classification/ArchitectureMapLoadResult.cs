using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Classification;

/// <summary>
/// Contains the validated architecture map and any diagnostics produced while
/// loading the project configuration.
/// </summary>
public sealed class ArchitectureMapLoadResult
{
    public ArchitectureMapConfiguration Configuration { get; init; } = new();

    public List<ArchitectureClassificationDiagnostic> Diagnostics { get; init; } = [];
}
