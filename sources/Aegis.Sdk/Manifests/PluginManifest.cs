namespace Aegis.Sdk.Manifests;

/// <summary>
/// Represents descriptive metadata for a discovered Aegis plugin.
/// </summary>
public sealed class PluginManifest
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string Author { get; init; } = "Unknown";
    public string Description { get; init; } = string.Empty;
    public string SupportedLanguages { get; init; } = "All";
    public string AssemblyName { get; init; } = string.Empty;
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.UtcNow;

    public override string ToString() => $"{Name} v{Version} by {Author}";
}
