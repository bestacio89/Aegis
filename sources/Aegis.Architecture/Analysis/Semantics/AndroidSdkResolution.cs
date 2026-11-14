namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>Resolved local Android SDK evidence for one module's declared compile SDK.</summary>
public sealed record AndroidSdkResolution(
    string? SdkRoot,
    string? PlatformDirectory,
    string? AndroidJar,
    string? CompileSdk,
    string SdkRootSource,
    bool IsComplete,
    IReadOnlyList<Aegis.Shared.Architecture.Models.AnalysisDiagnostic> Diagnostics);
