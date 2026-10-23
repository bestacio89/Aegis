using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class LayerDetector
{
    public static void Analyze(ProjectArchitectureContext context, IReadOnlyCollection<string> files)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(files);

        context.Layers.Clear();

        foreach (var module in context.Modules)
            module.Layers.Clear();

        var normalizedModules = context.Modules
            .Select(module => new
            {
                Module = module,
                Path = Path.GetFullPath(module.Path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            })
            .OrderByDescending(item => item.Path.Length)
            .ToArray();

        var filesByModule = context.Modules.ToDictionary(
            module => module,
            _ => new List<string>());

        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(file);
            var owner = normalizedModules.FirstOrDefault(item =>
                string.Equals(fullPath, item.Path, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(
                    item.Path + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));

            if (owner is not null)
                filesByModule[owner.Module].Add(file);
        }

        var moduleLayers = new List<ProjectLayerContext>();

        foreach (var module in context.Modules)
        {
            var layer = new ProjectLayerContext
            {
                Name = ResolveLayerName(module),
                Path = module.Path,
                Files = filesByModule[module],
                Confidence = IsKnownLayer(module.Type) ? 0.95 : 0.5
            };

            module.Layers.Add(layer);
            moduleLayers.Add(layer);
        }

        // A project/module is not itself an architectural layer. The top-level
        // inventory contains distinct semantic roles, with their owned files
        // aggregated, while each module retains its own path-specific layer view.
        foreach (var group in moduleLayers.GroupBy(
                     layer => layer.Name,
                     StringComparer.OrdinalIgnoreCase))
        {
            context.Layers.Add(new ProjectLayerContext
            {
                Name = group.Key,
                Path = context.RootPath,
                Files = group.SelectMany(layer => layer.Files)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Confidence = group.Min(layer => layer.Confidence)
            });
        }
    }

    private static string ResolveLayerName(ProjectModuleContext module) =>
        module.Type?.Trim().ToLowerInvariant() switch
        {
            "api" => "Api",
            "application" => "Application",
            "domain" => "Domain",
            "infrastructure" => "Infrastructure",
            "contracts" => "Contracts",
            "presentation" => "Presentation",
            "consumer" => "Consumer",
            "client" => "Client",
            "test" => "Tests",
            "unity" => "Client",
            _ => $"Unclassified:{module.Name}"
        };

    private static bool IsKnownLayer(string? type) =>
        type is not null
        && type.Trim().ToLowerInvariant() is
            "api" or "application" or "domain" or "infrastructure"
            or "contracts" or "presentation" or "consumer" or "client" or "test" or "unity";
}
