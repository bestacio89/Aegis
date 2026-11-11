using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis;

/// <summary>
/// Determines whether a repository has enough distinct architectural roles
/// for layer-boundary analysis to produce meaningful results.
/// </summary>
internal static class ArchitectureLayerApplicability
{
    public static bool IsMultiProjectWithoutLayerBoundaryModel(
        ProjectArchitectureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Modules.Count == 0
            || context.DiscoveryDiagnostics.Any(diagnostic => diagnostic.AffectsCoverage))
        {
            return false;
        }

        var isMultiProjectLibraryOrMixed =
            string.Equals(
                context.ArchitectureStyle,
                "Multi-Project / Mixed",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                context.ArchitectureStyle,
                "Multi-Project Library",
                StringComparison.OrdinalIgnoreCase);

        if (!isMultiProjectLibraryOrMixed)
            return false;

        var architecturalRoles = context.Layers
            .Select(layer => layer.Name?.Trim())
            .Where(name =>
                !string.IsNullOrWhiteSpace(name)
                && !name.StartsWith(
                    "Unclassified:",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    name,
                    "Tests",
                    StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Count();

        // A single role (for example, Client) does not define a layer-boundary
        // model. At least two distinct roles are required to assess direction.
        return architecturalRoles < 2;
    }
}
