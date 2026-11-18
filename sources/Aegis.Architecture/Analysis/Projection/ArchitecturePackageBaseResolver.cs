namespace Aegis.Architecture.Analysis.Projection;

/// <summary>
/// Resolves the package root used by hierarchical package presentation. The caller
/// supplies an application-entry package when native evidence identifies one.
/// </summary>
public static class ArchitecturePackageBaseResolver
{
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

        return string.Join('.', packages[0].Take(prefixLength));
    }

    private static string Normalize(string packageName) =>
        string.Join('.', packageName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
