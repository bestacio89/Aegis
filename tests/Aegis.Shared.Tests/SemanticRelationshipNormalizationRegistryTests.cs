using System.IO;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class SemanticRelationshipNormalizationRegistryTests
{
    [Theory]
    [InlineData(SemanticRelationshipKind.Declares, ArchitectureRelationshipKind.Declares)]
    [InlineData(SemanticRelationshipKind.Inherits, ArchitectureRelationshipKind.Inherits)]
    [InlineData(SemanticRelationshipKind.Implements, ArchitectureRelationshipKind.Implements)]
    [InlineData(SemanticRelationshipKind.Calls, ArchitectureRelationshipKind.Calls)]
    [InlineData(SemanticRelationshipKind.References, ArchitectureRelationshipKind.References)]
    [InlineData(SemanticRelationshipKind.UsesAnnotation, ArchitectureRelationshipKind.UsesAnnotation)]
    [InlineData(SemanticRelationshipKind.Injects, ArchitectureRelationshipKind.DependencyInjection)]
    [InlineData(SemanticRelationshipKind.Registers, ArchitectureRelationshipKind.Registration)]
    [InlineData(SemanticRelationshipKind.LoadsDynamically, ArchitectureRelationshipKind.DynamicLoading)]
    [InlineData(SemanticRelationshipKind.Actualizes, ArchitectureRelationshipKind.Actualizes)]
    [InlineData(SemanticRelationshipKind.DependsOn, ArchitectureRelationshipKind.Unknown)]
    [InlineData(SemanticRelationshipKind.Unknown, ArchitectureRelationshipKind.Unknown)]
    public void Maps_native_kinds_without_promoting_generic_dependencies(
        SemanticRelationshipKind providerKind,
        ArchitectureRelationshipKind expectedKind)
    {
        Assert.Equal(expectedKind, SemanticRelationshipKindMapper.Map(providerKind));
    }

    [Fact]
    public void Registry_preserves_each_occurrence_and_resolves_endpoints_only_within_its_run()
    {
        var firstRun = CreateRun(
            "first",
            "Java",
            ProjectPath("first"),
            SemanticAnalysisStatus.PartiallyCompleted,
            [
                CreateSymbol("source", "sample.Source", "Java", SemanticSymbolOrigin.Source),
                CreateSymbol("target", "sample.Target", "Java", SemanticSymbolOrigin.Source)
            ],
            [
                CreateRelationship("relationship-a", "source", "target", SemanticRelationshipKind.Calls),
                CreateRelationship("relationship-a", "source", "target", SemanticRelationshipKind.Calls)
            ]);
        var secondRun = CreateRun(
            "second",
            "Java",
            ProjectPath("second"),
            SemanticAnalysisStatus.Failed,
            [
                CreateSymbol("source", "sample.Source", "Java", SemanticSymbolOrigin.Source),
                CreateSymbol("target", "sample.Target", "Java", SemanticSymbolOrigin.Source)
            ],
            [
                CreateRelationship("relationship-a", "source", "target", SemanticRelationshipKind.References)
            ]);

        var runs = new[] { firstRun, secondRun };
        var relationshipRegistry = SemanticRelationshipEvidenceRegistry.Create(runs);
        var symbolIndex = SemanticSymbolIndex.Create(runs);
        var registry = SemanticRelationshipNormalizationRegistry.Create(relationshipRegistry, symbolIndex);

        Assert.Equal(3, registry.Count);
        Assert.Equal(2, registry.ForRun(0).Count);
        Assert.Single(registry.ForRun(1));
        Assert.Equal(2, registry.ForKind(ArchitectureRelationshipKind.Calls).Count);
        Assert.Single(registry.ForKind(ArchitectureRelationshipKind.References));
        Assert.All(registry.ForRun(0), item =>
        {
            Assert.NotNull(item.Source);
            Assert.NotNull(item.Target);
            Assert.NotNull(item.SourceIdentity);
            Assert.NotNull(item.TargetIdentity);
            Assert.Equal(SemanticResolutionStatus.Resolved, item.ResolutionStatus);
            Assert.Equal(SemanticAnalysisStatus.PartiallyCompleted, item.Evidence.RunStatus);
        });
        Assert.Equal(0, registry.ForRun(0)[0].Evidence.RelationshipIndex);
        Assert.Equal(1, registry.ForRun(0)[1].Evidence.RelationshipIndex);
        Assert.Equal("second", registry.ForRun(1)[0].Evidence.RunId);
        Assert.Equal(SemanticAnalysisStatus.Failed, registry.ForRun(1)[0].Evidence.RunStatus);
    }

    [Fact]
    public void Registry_retains_ownerless_external_and_ambiguous_endpoints_without_inventing_identity()
    {
        var run = CreateRun(
            "external",
            "Kotlin",
            ProjectPath("consumer"),
            SemanticAnalysisStatus.Completed,
            [
                CreateSymbol("source", "sample.Source", "Kotlin", SemanticSymbolOrigin.Source),
                CreateSymbol("duplicate", "sample.DuplicateA", "Kotlin", SemanticSymbolOrigin.Source),
                CreateSymbol("duplicate", "sample.DuplicateB", "Kotlin", SemanticSymbolOrigin.Source),
                CreateSymbol("external", "library.External", "Java", SemanticSymbolOrigin.External)
            ],
            [
                CreateRelationship("ambiguous", "duplicate", "external", SemanticRelationshipKind.Calls),
                CreateRelationship("missing", "missing-source", "missing-target", SemanticRelationshipKind.Unknown)
            ]);

        var runs = new[] { run };
        var registry = SemanticRelationshipNormalizationRegistry.Create(
            SemanticRelationshipEvidenceRegistry.Create(runs),
            SemanticSymbolIndex.Create(runs));

        var ambiguous = registry.ForRun(0)[0];
        Assert.True(ambiguous.IsSourceAmbiguous);
        Assert.Null(ambiguous.Source);
        Assert.NotNull(ambiguous.Target);
        Assert.Null(ambiguous.TargetIdentity);

        var missing = registry.ForRun(0)[1];
        Assert.False(missing.IsSourceAmbiguous);
        Assert.False(missing.IsTargetAmbiguous);
        Assert.Null(missing.Source);
        Assert.Null(missing.Target);
        Assert.Equal(ArchitectureRelationshipKind.Unknown, missing.Kind);
    }

    [Fact]
    public void Registry_uses_explicit_external_ownership_when_provider_supplies_it()
    {
        var run = CreateRun(
            "external-owned",
            "Kotlin",
            ProjectPath("consumer"),
            SemanticAnalysisStatus.Completed,
            [
                CreateSymbol("source", "sample.Source", "Kotlin", SemanticSymbolOrigin.Source),
                new SemanticSymbol
                {
                    Id = "external",
                    QualifiedName = "library.External",
                    Language = "Java",
                    Origin = SemanticSymbolOrigin.External,
                    OwningProjectPath = ProjectPath("library")
                }
            ],
            [CreateRelationship("owned", "source", "external", SemanticRelationshipKind.Calls)]);

        var runs = new[] { run };
        var registry = SemanticRelationshipNormalizationRegistry.Create(
            SemanticRelationshipEvidenceRegistry.Create(runs),
            SemanticSymbolIndex.Create(runs));

        var evidence = Assert.Single(registry.Evidence);
        Assert.Equal(ProjectPath("consumer"), evidence.SourceIdentity!.OwnerProjectPath);
        Assert.Equal(ProjectPath("library"), evidence.TargetIdentity!.OwnerProjectPath);
    }

    private static string ProjectPath(string name) =>
        Path.Combine(Path.GetTempPath(), "aegis-relationship-normalization-tests", name);

    private static SemanticAnalysisRun CreateRun(
        string runId,
        string language,
        string projectPath,
        SemanticAnalysisStatus status,
        IReadOnlyList<SemanticSymbol> symbols,
        IReadOnlyList<SemanticRelationship> relationships) =>
        new()
        {
            RunId = runId,
            Language = language,
            EngineId = "test-provider",
            ProjectPath = projectPath,
            Status = status,
            Symbols = [.. symbols],
            Relationships = [.. relationships]
        };

    private static SemanticSymbol CreateSymbol(
        string id,
        string qualifiedName,
        string language,
        SemanticSymbolOrigin origin) =>
        new()
        {
            Id = id,
            QualifiedName = qualifiedName,
            Language = language,
            Origin = origin,
            FilePath = origin == SemanticSymbolOrigin.Source ? ProjectPath("source.java") : null
        };

    private static SemanticRelationship CreateRelationship(
        string id,
        string sourceId,
        string targetId,
        SemanticRelationshipKind kind) =>
        new()
        {
            Id = id,
            SourceSymbolId = sourceId,
            TargetSymbolId = targetId,
            Kind = kind,
            ResolutionStatus = SemanticResolutionStatus.Resolved
        };
}
