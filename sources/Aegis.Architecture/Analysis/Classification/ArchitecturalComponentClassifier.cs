using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Classification;

/// <summary>
/// Classifies language-level code containers into logical architectural
/// components using explicit mappings first and conservative conventions second.
/// </summary>
public static class ArchitecturalComponentClassifier
{
    private const double ExplicitMappingConfidence = 1.0;
    private const double ConventionConfidence = 0.85;

    private static readonly string[] ConventionalRoles =
    [
        "domain",
        "application",
        "infrastructure",
        "presentation"
    ];

    public static void Classify(
        ProjectArchitectureContext context,
        ArchitectureMapConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        configuration ??= new ArchitectureMapConfiguration();

        context.ArchitecturalComponents.Clear();
        context.ArchitecturalComponentMemberships.Clear();
        context.ArchitectureClassificationDiagnostics.Clear();

        var diagnostics = new List<ArchitectureClassificationDiagnostic>();
        var mappings = ValidateConfiguration(configuration, diagnostics);
        var matchedMappingIds = new HashSet<string>(StringComparer.Ordinal);
        var componentMembers = new Dictionary<string, List<ArchitectureComponentContext>>(StringComparer.Ordinal);
        var componentMetadata = new Dictionary<string, (string Name, string? Role, string? Description)>(StringComparer.Ordinal);

        foreach (var codeComponent in context.ArchitectureComponents
                     .OrderBy(component => component.BuildModulePath, PathComparer)
                     .ThenBy(component => component.Language, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(component => component.QualifiedName, StringComparer.Ordinal))
        {
            var explicitMatch = FindExplicitMatch(codeComponent, context.RootPath, mappings);

            if (explicitMatch.IsAmbiguous)
            {
                foreach (var mapping in explicitMatch.Candidates)
                    matchedMappingIds.Add(mapping.Id);

                diagnostics.Add(new ArchitectureClassificationDiagnostic
                {
                    Code = "ArchitectureMapAmbiguousMatch",
                    Severity = "Warning",
                    Message = $"Code container '{codeComponent.QualifiedName}' matches multiple equally specific mappings with conflicting logical component targets.",
                    CodeComponentId = codeComponent.Id,
                    Language = codeComponent.Language,
                    ModulePath = codeComponent.BuildModulePath,
                    AffectsCoverage = true
                });

                context.ArchitecturalComponentMemberships.Add(CreateUnclassifiedMembership(
                    codeComponent,
                    "Conflicting explicit mappings prevent a deterministic classification."));
                continue;
            }

            if (explicitMatch.Mapping is not null)
            {
                var mapping = explicitMatch.Mapping;
                matchedMappingIds.Add(mapping.Id);
                AddMembership(
                    context,
                    componentMembers,
                    componentMetadata,
                    codeComponent,
                    mapping.ComponentId,
                    mapping.ComponentName,
                    mapping.ComponentRole,
                    mapping.Description,
                    ArchitectureClassificationSource.ExplicitMapping,
                    ExplicitMappingConfidence,
                    mapping.Id,
                    mapping.Value,
                    $"Explicit {mapping.MatchKind} mapping '{mapping.Id}' matched '{mapping.Value}'.");
                continue;
            }

            var inference = InferConvention(codeComponent);
            if (inference.Ambiguous)
            {
                diagnostics.Add(new ArchitectureClassificationDiagnostic
                {
                    Code = "ArchitectureClassificationConflictingConventions",
                    Severity = "Warning",
                    Message = $"Code container '{codeComponent.QualifiedName}' contains conflicting architectural convention signals.",
                    CodeComponentId = codeComponent.Id,
                    Language = codeComponent.Language,
                    ModulePath = codeComponent.BuildModulePath,
                    AffectsCoverage = true
                });

                context.ArchitecturalComponentMemberships.Add(CreateUnclassifiedMembership(
                    codeComponent,
                    "Conflicting convention signals require an explicit architecture mapping."));
                continue;
            }

            if (inference.Role is not null)
            {
                var componentId = $"convention:{inference.Role}";
                var componentName = ToDisplayName(inference.Role);
                AddMembership(
                    context,
                    componentMembers,
                    componentMetadata,
                    codeComponent,
                    componentId,
                    componentName,
                    componentName,
                    null,
                    ArchitectureClassificationSource.Convention,
                    ConventionConfidence,
                    null,
                    inference.Role,
                    $"The code-container identity contains the exact architectural segment '{inference.Role}'.");
                continue;
            }

            context.ArchitecturalComponentMemberships.Add(CreateUnclassifiedMembership(
                codeComponent,
                "No explicit mapping or sufficiently strong architectural convention was found."));

            diagnostics.Add(new ArchitectureClassificationDiagnostic
            {
                Code = "ArchitectureCodeContainerUnclassified",
                Severity = "Information",
                Message = $"Code container '{codeComponent.QualifiedName}' has no reliable logical architectural classification.",
                CodeComponentId = codeComponent.Id,
                Language = codeComponent.Language,
                ModulePath = codeComponent.BuildModulePath,
                AffectsCoverage = true
            });
        }

        foreach (var mapping in mappings.Where(mapping => !matchedMappingIds.Contains(mapping.Id)))
        {
            diagnostics.Add(new ArchitectureClassificationDiagnostic
            {
                Code = "ArchitectureMapMappingUnmatched",
                Severity = "Warning",
                Message = $"Architecture mapping '{mapping.Id}' did not match any discovered code container.",
                MappingId = mapping.Id,
                Language = mapping.Language,
                ModulePath = mapping.ModulePath,
                AffectsCoverage = true
            });
        }

        foreach (var pair in componentMembers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var members = pair.Value;
            var metadata = componentMetadata[pair.Key];

            context.ArchitecturalComponents.Add(new ArchitecturalComponentContext
            {
                Id = pair.Key,
                Name = metadata.Name,
                Role = metadata.Role,
                Description = metadata.Description,
                CodeContainerCount = members.Count,
                ProductionSymbolCount = members.Sum(component => component.ProductionSymbolCount),
                TestSymbolCount = members.Sum(component => component.TestSymbolCount)
            });
        }

        context.ArchitecturalComponentMemberships = context.ArchitecturalComponentMemberships
            .OrderBy(membership => membership.CodeComponentId, StringComparer.Ordinal)
            .ToList();
        context.ArchitectureClassificationDiagnostics = diagnostics
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.CodeComponentId, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.MappingId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<ArchitectureComponentMapping> ValidateConfiguration(
        ArchitectureMapConfiguration configuration,
        ICollection<ArchitectureClassificationDiagnostic> diagnostics)
    {
        if (configuration.SchemaVersion != ArchitectureMapConfiguration.CurrentSchemaVersion)
        {
            diagnostics.Add(new ArchitectureClassificationDiagnostic
            {
                Code = "ArchitectureMapUnsupportedSchema",
                Severity = "Warning",
                Message = $"Architecture map schema version {configuration.SchemaVersion} is not supported.",
                AffectsCoverage = true
            });
            return [];
        }

        var validMappings = new List<ArchitectureComponentMapping>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mapping in configuration.Mappings ?? [])
        {
            if (string.IsNullOrWhiteSpace(mapping.Id)
                || string.IsNullOrWhiteSpace(mapping.ComponentId)
                || string.IsNullOrWhiteSpace(mapping.ComponentName)
                || string.IsNullOrWhiteSpace(mapping.Value)
                || !Enum.IsDefined(mapping.MatchKind))
            {
                diagnostics.Add(new ArchitectureClassificationDiagnostic
                {
                    Code = "ArchitectureMapInvalidMapping",
                    Severity = "Warning",
                    Message = "An architecture mapping is missing a required field or uses an unsupported match kind.",
                    MappingId = mapping.Id,
                    AffectsCoverage = true
                });
                continue;
            }

            if (!seenIds.Add(mapping.Id))
            {
                diagnostics.Add(new ArchitectureClassificationDiagnostic
                {
                    Code = "ArchitectureMapDuplicateMappingId",
                    Severity = "Warning",
                    Message = $"Architecture mapping identifier '{mapping.Id}' is duplicated.",
                    MappingId = mapping.Id,
                    AffectsCoverage = true
                });
                continue;
            }

            validMappings.Add(mapping);
        }

        var conflictingComponentIds = validMappings
            .GroupBy(mapping => mapping.ComponentId, StringComparer.Ordinal)
            .Where(group => group
                .Select(mapping => $"{mapping.ComponentName}\u001f{mapping.ComponentRole}")
                .Distinct(StringComparer.Ordinal)
                .Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var componentId in conflictingComponentIds.OrderBy(value => value, StringComparer.Ordinal))
        {
            diagnostics.Add(new ArchitectureClassificationDiagnostic
            {
                Code = "ArchitectureMapComponentMetadataConflict",
                Severity = "Warning",
                Message = $"Mappings for logical component '{componentId}' disagree on component name or role. All mappings for this component were ignored.",
                MappingId = componentId,
                AffectsCoverage = true
            });
        }

        return validMappings
            .Where(mapping => !conflictingComponentIds.Contains(mapping.ComponentId))
            .OrderBy(mapping => mapping.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static ExplicitMatch FindExplicitMatch(
        ArchitectureComponentContext component,
        string projectRoot,
        IReadOnlyCollection<ArchitectureComponentMapping> mappings)
    {
        var candidates = mappings
            .Where(mapping => ScopeMatches(mapping, component, projectRoot))
            .Where(mapping => MappingMatches(mapping, component, projectRoot))
            .Select(mapping => new MappingCandidate(
                mapping,
                mapping.MatchKind switch
                {
                    ArchitectureMappingMatchKind.Exact => 0,
                    ArchitectureMappingMatchKind.Prefix => 1,
                    ArchitectureMappingMatchKind.Module => 2,
                    _ => int.MaxValue
                },
                mapping.MatchKind == ArchitectureMappingMatchKind.Prefix
                    ? mapping.Value.Length
                    : 0))
            .OrderBy(candidate => candidate.Precedence)
            .ThenByDescending(candidate => candidate.Specificity)
            .ThenBy(candidate => candidate.Mapping.Id, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
            return ExplicitMatch.None;

        var best = candidates[0];
        var equallySpecific = candidates
            .Where(candidate => candidate.Precedence == best.Precedence
                && candidate.Specificity == best.Specificity)
            .Select(candidate => candidate.Mapping)
            .ToArray();

        var conflictingTargets = equallySpecific
            .Select(mapping => $"{mapping.ComponentId}\u001f{mapping.ComponentName}\u001f{mapping.ComponentRole}")
            .Distinct(StringComparer.Ordinal)
            .Count() > 1;

        return conflictingTargets
            ? new ExplicitMatch(null, true, equallySpecific)
            : new ExplicitMatch(equallySpecific[0], false, equallySpecific);
    }

    private static bool ScopeMatches(
        ArchitectureComponentMapping mapping,
        ArchitectureComponentContext component,
        string projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(mapping.Language)
            && !mapping.Language.Equals(component.Language, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(mapping.ModuleName)
            && !mapping.ModuleName.Equals(component.BuildModuleName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(mapping.ModulePath)
            || PathsEqual(mapping.ModulePath, component.BuildModulePath, projectRoot);
    }

    private static bool MappingMatches(
        ArchitectureComponentMapping mapping,
        ArchitectureComponentContext component,
        string projectRoot) =>
        mapping.MatchKind switch
        {
            ArchitectureMappingMatchKind.Exact =>
                mapping.Value.Equals(component.QualifiedName, StringComparison.OrdinalIgnoreCase),

            ArchitectureMappingMatchKind.Prefix =>
                IsSegmentPrefix(mapping.Value, component.QualifiedName),

            ArchitectureMappingMatchKind.Module =>
                mapping.Value.Equals(component.BuildModuleName, StringComparison.OrdinalIgnoreCase)
                || PathsEqual(mapping.Value, component.BuildModulePath, projectRoot),

            _ => false
        };

    private static bool IsSegmentPrefix(string prefix, string value)
    {
        if (prefix.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || value.Length <= prefix.Length)
        {
            return false;
        }

        return value[prefix.Length] is '.' or '/' or '\\';
    }

    private static bool PathsEqual(string left, string right, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            var root = Path.GetFullPath(projectRoot);
            var leftPath = Path.GetFullPath(left, root);
            var rightPath = Path.GetFullPath(right, root);
            return string.Equals(
                Path.TrimEndingDirectorySeparator(leftPath),
                Path.TrimEndingDirectorySeparator(rightPath),
                PathComparison);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static ConventionInference InferConvention(ArchitectureComponentContext component)
    {
        var tokens = new[]
            {
                component.QualifiedName,
                component.Name,
                component.BuildModuleName
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value.Split(
                ['.', '/', '\\', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => ConventionalRoles.Contains(value, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return tokens.Length switch
        {
            0 => new ConventionInference(null, false),
            1 => new ConventionInference(tokens[0], false),
            _ => new ConventionInference(null, true)
        };
    }

    private static void AddMembership(
        ProjectArchitectureContext context,
        IDictionary<string, List<ArchitectureComponentContext>> componentMembers,
        IDictionary<string, (string Name, string? Role, string? Description)> componentMetadata,
        ArchitectureComponentContext codeComponent,
        string componentId,
        string componentName,
        string? componentRole,
        string? description,
        ArchitectureClassificationSource source,
        double confidence,
        string? ruleId,
        string? matchedIdentity,
        string explanation)
    {
        if (!componentMembers.TryGetValue(componentId, out var members))
        {
            members = [];
            componentMembers.Add(componentId, members);
            componentMetadata.Add(componentId, (componentName, componentRole, description));
        }

        members.Add(codeComponent);
        context.ArchitecturalComponentMemberships.Add(new ArchitecturalComponentMembership
        {
            CodeComponentId = codeComponent.Id,
            ArchitecturalComponentId = componentId,
            ClassificationSource = source,
            Confidence = confidence,
            ClassificationRuleId = ruleId,
            MatchedIdentity = matchedIdentity,
            Explanation = explanation
        });
    }

    private static ArchitecturalComponentMembership CreateUnclassifiedMembership(
        ArchitectureComponentContext component,
        string explanation) =>
        new()
        {
            CodeComponentId = component.Id,
            ArchitecturalComponentId = null,
            ClassificationSource = ArchitectureClassificationSource.Unclassified,
            Confidence = 0,
            MatchedIdentity = component.QualifiedName,
            Explanation = explanation
        };

    private static string ToDisplayName(string role) =>
        role.Length == 0 ? role : char.ToUpperInvariant(role[0]) + role[1..];

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed record MappingCandidate(
        ArchitectureComponentMapping Mapping,
        int Precedence,
        int Specificity);

    private sealed record ExplicitMatch(
        ArchitectureComponentMapping? Mapping,
        bool IsAmbiguous,
        IReadOnlyCollection<ArchitectureComponentMapping> Candidates)
    {
        public static ExplicitMatch None { get; } = new(null, false, []);
    }

    private sealed record ConventionInference(string? Role, bool Ambiguous);
}
