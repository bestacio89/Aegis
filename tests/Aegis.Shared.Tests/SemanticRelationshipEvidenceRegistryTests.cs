using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class SemanticRelationshipEvidenceRegistryTests
{
    [Fact]
    public void Registry_retains_every_relationship_occurrence_without_filtering_or_collapsing_provider_ids()
    {
        var duplicate = new SemanticRelationship
        {
            Id = "provider-id",
            SourceSymbolId = "source",
            TargetSymbolId = "target",
            Kind = SemanticRelationshipKind.Calls,
            ResolutionStatus = SemanticResolutionStatus.Resolved
        };
        var unresolved = new SemanticRelationship
        {
            Id = "provider-id",
            SourceSymbolId = "source",
            UnresolvedTarget = "external.Missing",
            Kind = SemanticRelationshipKind.References,
            ResolutionStatus = SemanticResolutionStatus.Unresolved
        };
        var withoutId = new SemanticRelationship
        {
            SourceSymbolId = "source",
            Kind = SemanticRelationshipKind.Unknown,
            ResolutionStatus = SemanticResolutionStatus.NotApplicable
        };
        var failedRunRelationship = new SemanticRelationship
        {
            Id = "failed-run-observation",
            Kind = SemanticRelationshipKind.DependsOn,
            ResolutionStatus = SemanticResolutionStatus.Unresolved
        };
        var runs = new[]
        {
            new SemanticAnalysisRun
            {
                RunId = "same-run-id",
                Language = "Java",
                EngineId = "jdt",
                ProjectPath = "/workspace/java",
                Status = SemanticAnalysisStatus.Completed,
                Relationships = [duplicate, duplicate, unresolved, withoutId]
            },
            new SemanticAnalysisRun
            {
                RunId = "same-run-id",
                Language = "Kotlin",
                EngineId = "kotlin-k2",
                ProjectPath = "/workspace/kotlin",
                Status = SemanticAnalysisStatus.Failed,
                Relationships = [failedRunRelationship]
            }
        };

        var registry = SemanticRelationshipEvidenceRegistry.Create(runs);

        Assert.Equal(5, registry.Count);
        Assert.Equal(4, registry.ForRun(0).Count);
        Assert.Single(registry.ForRun(1));
        Assert.Equal(5, registry.Evidence.Select(item => item.EvidenceId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal([0, 1, 2, 3], registry.ForRun(0).Select(item => item.RelationshipIndex).ToArray());
        Assert.Same(duplicate, registry.ForRun(0)[0].Relationship);
        Assert.Same(duplicate, registry.ForRun(0)[1].Relationship);
        Assert.Same(unresolved, registry.ForRun(0)[2].Relationship);
        Assert.Same(withoutId, registry.ForRun(0)[3].Relationship);
        Assert.Same(failedRunRelationship, registry.ForRun(1)[0].Relationship);
        Assert.Equal("Java", registry.ForRun(0)[0].Language);
        Assert.Equal("Kotlin", registry.ForRun(1)[0].Language);
        Assert.Null(registry.ForRun(0)[3].Relationship.Id);
    }

    [Fact]
    public void Registry_supports_exact_lookup_for_duplicate_and_missing_provider_ids()
    {
        var run = new SemanticAnalysisRun
        {
            RunId = "run",
            Language = "Java",
            EngineId = "jdt",
            ProjectPath = "/workspace",
            Relationships =
            [
                new SemanticRelationship { Id = "duplicate" },
                new SemanticRelationship { Id = "duplicate" },
                new SemanticRelationship()
            ]
        };

        var registry = SemanticRelationshipEvidenceRegistry.Create([run]);

        Assert.True(registry.TryGet("0:0", out var first));
        Assert.True(registry.TryGet("0:1", out var second));
        Assert.True(registry.TryGet("0:2", out var missingId));
        Assert.NotSame(first, second);
        Assert.Equal("duplicate", first!.Relationship.Id);
        Assert.Equal("duplicate", second!.Relationship.Id);
        Assert.Null(missingId!.Relationship.Id);
        Assert.False(registry.TryGet("1:0", out _));
    }

    [Fact]
    public void Registry_handles_empty_run_collections_and_runs_without_relationships()
    {
        Assert.Empty(SemanticRelationshipEvidenceRegistry.Create([]).Evidence);

        var registry = SemanticRelationshipEvidenceRegistry.Create(
        [
            new SemanticAnalysisRun { RunId = "empty-run" }
        ]);

        Assert.Empty(registry.Evidence);
        Assert.Empty(registry.ForRun(0));
        Assert.Empty(registry.ForRun(1));
    }
}
