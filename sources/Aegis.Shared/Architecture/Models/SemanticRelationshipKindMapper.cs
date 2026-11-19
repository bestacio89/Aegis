using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Maps provider-native semantic relationship kinds to the shared architecture
/// taxonomy without changing or discarding the original provider classification.
/// </summary>
public static class SemanticRelationshipKindMapper
{
    public static ArchitectureRelationshipKind Map(SemanticRelationshipKind kind) =>
        kind switch
        {
            SemanticRelationshipKind.Declares => ArchitectureRelationshipKind.Declares,
            SemanticRelationshipKind.Inherits => ArchitectureRelationshipKind.Inherits,
            SemanticRelationshipKind.Implements => ArchitectureRelationshipKind.Implements,
            SemanticRelationshipKind.Calls => ArchitectureRelationshipKind.Calls,
            SemanticRelationshipKind.References => ArchitectureRelationshipKind.References,
            SemanticRelationshipKind.UsesAnnotation => ArchitectureRelationshipKind.UsesAnnotation,
            SemanticRelationshipKind.Injects => ArchitectureRelationshipKind.DependencyInjection,
            SemanticRelationshipKind.Registers => ArchitectureRelationshipKind.Registration,
            SemanticRelationshipKind.LoadsDynamically => ArchitectureRelationshipKind.DynamicLoading,
            SemanticRelationshipKind.Actualizes => ArchitectureRelationshipKind.Actualizes,
            // DependsOn is intentionally not mapped: it is too generic to assert
            // a specific native semantic relationship.
            _ => ArchitectureRelationshipKind.Unknown
        };
}
