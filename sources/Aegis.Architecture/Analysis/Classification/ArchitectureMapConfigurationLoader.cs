using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Classification;

/// <summary>
/// Loads the optional, declarative .aegis/architecture-map.json file.
/// No project scripts or configuration expressions are evaluated.
/// </summary>
public static class ArchitectureMapConfigurationLoader
{
    public const string RelativeConfigurationPath = ".aegis/architecture-map.json";
    private const long MaximumConfigurationBytes = 1_048_576;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<ArchitectureMapLoadResult> LoadAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var rootPath = Path.GetFullPath(projectRoot);
        var configurationPath = Path.Combine(
            rootPath,
            RelativeConfigurationPath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(configurationPath))
            return new ArchitectureMapLoadResult();

        var diagnostics = new List<ArchitectureClassificationDiagnostic>();

        try
        {
            var fileInfo = new FileInfo(configurationPath);
            if (fileInfo.Length > MaximumConfigurationBytes)
            {
                diagnostics.Add(CreateDiagnostic(
                    "ArchitectureMapTooLarge",
                    $"Architecture map exceeds the {MaximumConfigurationBytes} byte limit.",
                    configurationPath));
                return new ArchitectureMapLoadResult { Diagnostics = diagnostics };
            }

            await using var stream = new FileStream(
                configurationPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var configuration = await JsonSerializer.DeserializeAsync<ArchitectureMapConfiguration>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            if (configuration is null)
            {
                diagnostics.Add(CreateDiagnostic(
                    "ArchitectureMapEmpty",
                    "Architecture map must contain a JSON object.",
                    configurationPath));
                return new ArchitectureMapLoadResult { Diagnostics = diagnostics };
            }

            if (configuration.SchemaVersion != ArchitectureMapConfiguration.CurrentSchemaVersion)
            {
                diagnostics.Add(CreateDiagnostic(
                    "ArchitectureMapUnsupportedSchema",
                    $"Architecture map schema version {configuration.SchemaVersion} is not supported. Expected version {ArchitectureMapConfiguration.CurrentSchemaVersion}.",
                    configurationPath));
                return new ArchitectureMapLoadResult { Diagnostics = diagnostics };
            }

            var validMappings = ValidateMappings(configuration.Mappings ?? [], diagnostics);
            return new ArchitectureMapLoadResult
            {
                Configuration = new ArchitectureMapConfiguration
                {
                    SchemaVersion = configuration.SchemaVersion,
                    Mappings = validMappings
                },
                Diagnostics = diagnostics
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                "ArchitectureMapInvalidJson",
                $"Architecture map could not be parsed: {exception.Message}",
                configurationPath));
        }
        catch (IOException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                "ArchitectureMapReadFailed",
                $"Architecture map could not be read: {exception.Message}",
                configurationPath));
        }
        catch (UnauthorizedAccessException exception)
        {
            diagnostics.Add(CreateDiagnostic(
                "ArchitectureMapReadDenied",
                $"Architecture map access was denied: {exception.Message}",
                configurationPath));
        }

        return new ArchitectureMapLoadResult { Diagnostics = diagnostics };
    }

    private static List<ArchitectureComponentMapping> ValidateMappings(
        IReadOnlyCollection<ArchitectureComponentMapping?> mappings,
        ICollection<ArchitectureClassificationDiagnostic> diagnostics)
    {
        var nonNullMappings = mappings.Where(mapping => mapping is not null)
            .Cast<ArchitectureComponentMapping>()
            .ToArray();

        if (nonNullMappings.Length != mappings.Count)
        {
            diagnostics.Add(CreateDiagnostic(
                "ArchitectureMapNullMapping",
                "Architecture map contains a null mapping entry.",
                null));
        }

        var valid = new List<ArchitectureComponentMapping>();
        var mappingIds = new HashSet<string>(StringComparer.Ordinal);
        var invalidComponentIds = nonNullMappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.ComponentId))
            .GroupBy(mapping => mapping.ComponentId, StringComparer.Ordinal)
            .Where(group => group
                .Select(mapping => $"{mapping.ComponentName}\u001f{mapping.ComponentRole}")
                .Distinct(StringComparer.Ordinal)
                .Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var componentId in invalidComponentIds.OrderBy(value => value, StringComparer.Ordinal))
        {
            diagnostics.Add(CreateDiagnostic(
                "ArchitectureMapComponentMetadataConflict",
                $"Mappings for logical component '{componentId}' disagree on component name or role.",
                componentId));
        }

        foreach (var mapping in nonNullMappings)
        {
            var reason = ValidateMapping(mapping);
            if (reason is not null)
            {
                diagnostics.Add(CreateDiagnostic(
                    "ArchitectureMapInvalidMapping",
                    $"Mapping '{mapping.Id}' is invalid: {reason}",
                    mapping.Id));
                continue;
            }

            if (!mappingIds.Add(mapping.Id))
            {
                diagnostics.Add(CreateDiagnostic(
                    "ArchitectureMapDuplicateMappingId",
                    $"Mapping identifier '{mapping.Id}' is duplicated.",
                    mapping.Id));
                continue;
            }

            if (invalidComponentIds.Contains(mapping.ComponentId))
                continue;

            valid.Add(mapping);
        }

        return valid
            .OrderBy(mapping => mapping.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static string? ValidateMapping(ArchitectureComponentMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping.Id))
            return "Id is required.";
        if (string.IsNullOrWhiteSpace(mapping.ComponentId))
            return "ComponentId is required.";
        if (string.IsNullOrWhiteSpace(mapping.ComponentName))
            return "ComponentName is required.";
        if (string.IsNullOrWhiteSpace(mapping.Value))
            return "Value is required.";
        if (!Enum.IsDefined(mapping.MatchKind))
            return "MatchKind is not supported.";

        return null;
    }

    private static ArchitectureClassificationDiagnostic CreateDiagnostic(
        string code,
        string message,
        string? target) =>
        new()
        {
            Code = code,
            Severity = "Warning",
            Message = message,
            Target = target,
            AffectsCoverage = true
        };
}
