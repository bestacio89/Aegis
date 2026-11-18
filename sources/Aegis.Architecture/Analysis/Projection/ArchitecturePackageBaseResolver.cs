using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Projection;

/// <summary>
/// Resolves the package root used by hierarchical package presentation. Native
/// Spring Boot annotation evidence takes precedence over the branch-root fallback.
/// </summary>
public static class ArchitecturePackageBaseResolver
{
    private const string SpringBootApplicationType = "org.springframework.boot.autoconfigure.SpringBootApplication";

    public static string Resolve(ProjectArchitectureContext context, string? policyOverride = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var packageNames = context.ArchitectureComponents
            .Where(component => string.Equals(component.ComponentKind, "Package", StringComparison.OrdinalIgnoreCase))
            .Select(component => component.QualifiedName);

        var applicationPackage = FindSpringBootApplicationPackage(context.SemanticAnalysisRuns);
        return Resolve(packageNames, policyOverride, applicationPackage);
    }

    public static string Resolve(
        IEnumerable<string> packageNames,
        string? policyOverride = null,
        string? applicationEntryPackage = null)
    {
        ArgumentNullException.ThrowIfNull(packageNames);

        if (!string.IsNullOrWhiteSpace(policyOverride))
            return Normalize(policyOverride);

        if (!string.IsNullOrWhiteSpace(applicationEntryPackage))
            return Normalize(applicationEntryPackage);

        var packages = packageNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .Select(name => name.Split('.', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length > 0)
            .ToArray();

        if (packages.Length == 0)
            return string.Empty;

        var commonLength = packages.Min(parts => parts.Length);
        var prefixLength = 0;
        while (prefixLength < commonLength)
        {
            var segment = packages[0][prefixLength];
            if (packages.Skip(1).Any(parts => !string.Equals(parts[prefixLength], segment, StringComparison.Ordinal)))
                break;
            prefixLength++;
        }

        return string.Join(".", packages[0].Take(prefixLength));
    }

    private static string? FindSpringBootApplicationPackage(IEnumerable<SemanticAnalysisRun> runs)
    {
        var packages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in runs)
        {
            var symbolsById = run.Symbols
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Id))
                .GroupBy(symbol => symbol.Id, StringComparer.Ordinal)
                .Where(group => group.Count() == 1)
                .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

            foreach (var annotationRelationship in run.Relationships.Where(relationship =>
                         relationship.Kind == SemanticRelationshipKind.UsesAnnotation
                         && !string.IsNullOrWhiteSpace(relationship.SourceSymbolId)
                         && !string.IsNullOrWhiteSpace(relationship.TargetSymbolId)))
            {
                if (!symbolsById.TryGetValue(annotationRelationship.TargetSymbolId!, out var annotation)
                    || !IsSpringBootApplication(annotation)
                    || !symbolsById.TryGetValue(annotationRelationship.SourceSymbolId, out var applicationType)
                    || string.IsNullOrWhiteSpace(applicationType.ContainerName))
                    continue;

                packages.Add(Normalize(applicationType.ContainerName));
            }
        }

        return packages.Count == 1 ? packages.Single() : null;
    }

    private static bool IsSpringBootApplication(SemanticSymbol symbol) =>
        string.Equals(symbol.QualifiedName, SpringBootApplicationType, StringComparison.Ordinal)
        || (string.IsNullOrWhiteSpace(symbol.QualifiedName)
            && string.Equals(symbol.Name, "SpringBootApplication", StringComparison.Ordinal));

    private static string Normalize(string packageName) =>
        string.Join(".", packageName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
