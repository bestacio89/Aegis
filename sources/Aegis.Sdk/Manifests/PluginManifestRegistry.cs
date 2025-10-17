using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Aegis.Sdk.Manifests;

/// <summary>
/// Global registry of loaded Aegis plugin manifests.
/// Populated automatically during plugin discovery.
/// </summary>
public static class PluginManifestRegistry
{
    private static readonly List<PluginManifest> _manifests = new();

    public static IReadOnlyList<PluginManifest> Manifests => _manifests;

    public static void RegisterFromAssembly(Assembly assembly, ILogger? logger = null)
    {
        var manifestAttr = assembly.GetCustomAttribute<ManifestAttribute>();
        if (manifestAttr == null)
        {
            logger?.LogDebug("No manifest attribute found in assembly {Name}.", assembly.GetName().Name);
            return;
        }

        var manifest = new PluginManifest
        {
            Name = manifestAttr.Name,
            Version = manifestAttr.Version,
            Author = manifestAttr.Author,
            Description = manifestAttr.Description,
            SupportedLanguages = manifestAttr.SupportedLanguages,
            AssemblyName = assembly.GetName().Name ?? string.Empty,
            LoadedAt = DateTimeOffset.UtcNow
        };

        _manifests.Add(manifest);
        logger?.LogInformation("🧩 Registered plugin manifest: {Plugin}", manifest);
    }
}
