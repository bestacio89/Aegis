using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies.Architecture;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class LayerDetector
{
    public static void Analyze(
        ProjectArchitectureContext context,
        IReadOnlyCollection<string> files,
        ArchitecturePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(files);

        var layerHints = policy?.LayerHints
            ?? new ArchitecturePolicy().LayerHints;

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
            var (layerName, confidence) = ResolveLayer(
                module,
                filesByModule[module],
                layerHints);

            var layer = new ProjectLayerContext
            {
                Name = layerName,
                Path = module.Path,
                Files = filesByModule[module],
                Confidence = confidence
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

    private static (string Name, double Confidence) ResolveLayer(
        ProjectModuleContext module,
        IReadOnlyCollection<string> moduleFiles,
        IReadOnlyDictionary<string, string[]> layerHints)
    {
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddIdentifiers(identifiers, module.Name);
        AddIdentifiers(identifiers, Path.GetFileName(module.Path));

        if (!string.IsNullOrWhiteSpace(module.ProjectFile))
        {
            AddIdentifiers(
                identifiers,
                Path.GetFileNameWithoutExtension(module.ProjectFile));
        }

        foreach (var file in moduleFiles)
        {
            var relativePath = Path.GetRelativePath(module.Path, file);
            var directory = Path.GetDirectoryName(relativePath);

            if (string.IsNullOrWhiteSpace(directory))
                continue;

            foreach (var segment in directory.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                AddIdentifiers(identifiers, segment);
            }
        }

        var matchingLayers = layerHints
            .Where(pair =>
                !string.IsNullOrWhiteSpace(pair.Key)
                && pair.Value is not null
                && pair.Value.Any(hint => HintMatches(hint, identifiers)))
            .Select(pair => pair.Key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matchingLayers.Length == 1)
            return (matchingLayers[0], 0.9);

        if (matchingLayers.Length > 1)
            return ($"Unclassified:{module.Name}", 0.25);

        var inferredLayer = ResolveKnownLayerName(module.Type);
        return inferredLayer is null
            ? ($"Unclassified:{module.Name}", 0.5)
            : (inferredLayer, 0.95);
    }

    private static bool HintMatches(
        string? hint,
        IReadOnlySet<string> identifiers)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return false;

        var normalizedHint = NormalizeIdentifier(hint);
        if (normalizedHint.Length > 0 && identifiers.Contains(normalizedHint))
            return true;

        return SplitIdentifier(hint).Any(identifiers.Contains);
    }

    private static void AddIdentifiers(
        ISet<string> identifiers,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = NormalizeIdentifier(value);
        if (normalized.Length > 0)
            identifiers.Add(normalized);

        foreach (var token in SplitIdentifier(value))
            identifiers.Add(token);
    }

    private static IEnumerable<string> SplitIdentifier(string value)
    {
        var token = new System.Text.StringBuilder();

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];

            if (!char.IsLetterOrDigit(current))
            {
                if (token.Length > 0)
                {
                    yield return token.ToString().ToLowerInvariant();
                    token.Clear();
                }

                continue;
            }

            if (char.IsUpper(current)
                && token.Length > 0
                && (char.IsLower(value[index - 1]) || char.IsDigit(value[index - 1])))
            {
                yield return token.ToString().ToLowerInvariant();
                token.Clear();
            }

            token.Append(current);
        }

        if (token.Length > 0)
            yield return token.ToString().ToLowerInvariant();
    }

    private static string NormalizeIdentifier(string value) =>
        new(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static string? ResolveKnownLayerName(string? type) =>
        type?.Trim().ToLowerInvariant() switch
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
            _ => null
        };
}
