using System.Text.Json;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis;

/// <summary>
/// Persists user-authored application integration declarations in the repository's
/// .aegis configuration directory. Paths are stored relative to the repository root
/// so declarations remain portable when the repository is moved or cloned.
/// </summary>
public static class ApplicationIntegrationConfigurationStore
{
    private const string ConfigurationDirectory = ".aegis";
    private const string ConfigurationFileName = "application-integrations.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string GetConfigurationPath(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        return Path.Combine(
            Path.GetFullPath(projectRoot),
            ConfigurationDirectory,
            ConfigurationFileName);
    }

    public static List<ArchitectureIntegrationContext> Load(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var root = Path.GetFullPath(projectRoot);
        var configurationPath = GetConfigurationPath(root);
        if (!File.Exists(configurationPath))
            return [];

        var integrations = JsonSerializer.Deserialize<List<ArchitectureIntegrationContext>>(
            File.ReadAllText(configurationPath),
            JsonOptions) ?? [];

        foreach (var integration in integrations)
        {
            if (integration is null)
                throw new InvalidDataException("The application integration configuration contains a null entry.");

            integration.SourceModulePath = ResolveStoredPath(root, integration.SourceModulePath);
            integration.TargetModulePath = ResolveStoredPath(root, integration.TargetModulePath);

            if (PathsEqual(integration.SourceModulePath, integration.TargetModulePath))
                throw new InvalidDataException("An application integration cannot target its source module.");
        }

        return integrations;
    }

    public static void Save(
        string projectRoot,
        IReadOnlyCollection<ArchitectureIntegrationContext> integrations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(integrations);

        var root = Path.GetFullPath(projectRoot);
        var payload = integrations.Select(integration =>
        {
            ArgumentNullException.ThrowIfNull(integration);

            if (string.IsNullOrWhiteSpace(integration.SourceModuleName)
                || string.IsNullOrWhiteSpace(integration.TargetModuleName)
                || string.IsNullOrWhiteSpace(integration.SourceModulePath)
                || string.IsNullOrWhiteSpace(integration.TargetModulePath))
            {
                throw new InvalidDataException(
                    "Application integrations require named source and target modules with paths.");
            }

            var sourcePath = ToStoredPath(root, integration.SourceModulePath);
            var targetPath = ToStoredPath(root, integration.TargetModulePath);

            if (PathsEqual(sourcePath, targetPath))
                throw new InvalidDataException("An application integration cannot target its source module.");

            return new ArchitectureIntegrationContext
            {
                Id = integration.Id,
                SourceModuleName = integration.SourceModuleName,
                SourceModulePath = sourcePath,
                TargetModuleName = integration.TargetModuleName,
                TargetModulePath = targetPath,
                InteractionPattern = integration.InteractionPattern,
                Protocol = integration.Protocol,
                EvidenceOrigin = integration.EvidenceOrigin,
                EndpointResolutionStatus = integration.EndpointResolutionStatus,
                Notes = integration.Notes,
                DeclaredAtUtc = integration.DeclaredAtUtc
            };
        }).ToArray();

        var configurationPath = GetConfigurationPath(root);
        var directory = Path.GetDirectoryName(configurationPath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = configurationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(temporaryPath, configurationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string ToStoredPath(string root, string modulePath)
    {
        var fullPath = Path.GetFullPath(modulePath);
        if (!IsSameOrDescendantPath(fullPath, root))
        {
            throw new InvalidDataException(
                $"Application integration endpoint '{modulePath}' is outside the scanned repository.");
        }

        return Path.GetRelativePath(root, fullPath);
    }

    private static string ResolveStoredPath(string root, string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            throw new InvalidDataException("An application integration endpoint path is empty.");

        var fullPath = Path.GetFullPath(
            Path.IsPathRooted(storedPath)
                ? storedPath
                : Path.Combine(root, storedPath));

        if (!IsSameOrDescendantPath(fullPath, root))
        {
            throw new InvalidDataException(
                $"Stored application integration endpoint '{storedPath}' escapes the repository root.");
        }

        return fullPath;
    }

    private static bool IsSameOrDescendantPath(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(normalizedPath, normalizedRoot, comparison)
            || normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison);
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }
}
