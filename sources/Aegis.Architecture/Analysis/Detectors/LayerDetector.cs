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
        var productionLayers = context.Modules
            .Where(module => !module.IsTestProject && !module.IsTestSupport)
            .SelectMany(module => module.Layers);

        foreach (var group in productionLayers.GroupBy(
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
        var projectIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(module.ProjectFile))
        {
            AddIdentifiers(projectIdentifiers, module.Name);
            AddIdentifiers(
                projectIdentifiers,
                Path.GetFileNameWithoutExtension(module.ProjectFile));
        }

        // Explicit project semantics take precedence over broad naming hints.
        // For example, a foundational "Base.Abstractions" module is classified
        // as Infrastructure by module discovery; a generic "abstractions"
        // hint must not incorrectly remap it to Contracts.
        var inferredLayer = ResolveKnownLayerName(module.Type);

        if (inferredLayer is not null)
            return (inferredLayer, 0.95);

        var projectMatches = FindMatchingLayers(layerHints, projectIdentifiers);

        if (projectMatches.Length == 1)
            return (projectMatches[0], 0.9);

        if (projectMatches.Length > 1)
            return ($"Unclassified:{module.Name}", 0.25);

        var folderIdentifiers = new HashSet<string>(
            projectIdentifiers,
            StringComparer.OrdinalIgnoreCase);

        AddIdentifiers(folderIdentifiers, Path.GetFileName(module.Path));

        foreach (var file in moduleFiles)
        {
            var relativePath = Path.GetRelativePath(
                Path.GetFullPath(module.Path),
                Path.GetFullPath(file));
            var directory = Path.GetDirectoryName(relativePath);

            if (string.IsNullOrWhiteSpace(directory))
                continue;

            foreach (var segment in directory.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                AddIdentifiers(folderIdentifiers, segment);
            }
        }

        var folderMatches = FindMatchingLayers(layerHints, folderIdentifiers);

        if (folderMatches.Length == 1)
            return (folderMatches[0], 0.85);

        if (folderMatches.Length > 1)
            return ($"Unclassified:{module.Name}", 0.25);

        return ($"Unclassified:{module.Name}", 0.5);
    }

    private static string[] FindMatchingLayers(
        IReadOnlyDictionary<string, string[]> layerHints,
        IReadOnlySet<string> identifiers) =>
        layerHints
            .Where(pair =>
                !string.IsNullOrWhiteSpace(pair.Key)
                && pair.Value is not null
                && pair.Value.Any(hint => HintMatches(hint, identifiers)))
            .Select(pair => pair.Key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool HintMatches(
        string? hint,
        IReadOnlySet<string> identifiers)
    {
        if (string.IsNullOrWhiteSpace(hint))
            return false;

        var normalizedHint = NormalizeIdentifier(hint);
        return normalizedHint.Length > 0 && identifiers.Contains(normalizedHint);
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
            "testsupport" => "Tests",
            "unity" => "Client",
            _ => null
        };
}
