using Aegis.Architecture.Analysis.Detectors;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureDependencyCycleAnalyzerTests
{
    [Fact]
    public void Detects_multi_node_code_container_cycle_and_preserves_evidence()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureComponentDependencies =
            [
                Edge("a", "b", "Production", "run-1", "ab"),
                Edge("b", "c", "Production", "run-1", "bc"),
                Edge("c", "a", "Production", "run-2", "ca"),
                Edge("c", "outside", "Production", "run-2", "out")
            ]
        };

        ArchitectureDependencyCycleAnalyzer.Analyze(context);

        var cycle = Assert.Single(context.ArchitectureComponentCycles);
        Assert.Equal(["a", "b", "c"], cycle.ComponentIds);
        Assert.Equal(3, cycle.DependencyEdgeCount);
        Assert.Equal(3, cycle.RelationshipCount);
        Assert.Equal(3, cycle.ContributingRelationships.Count);
        Assert.False(cycle.IsSingleNodeCycle);
        Assert.Empty(context.ArchitecturalComponentCycles);
    }

    [Fact]
    public void Detects_self_dependency_as_a_single_node_cycle()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureComponentInternalDependencies =
            [
                Edge("package-a", "package-a", "Production", "run-1", "self")
            ]
        };

        ArchitectureDependencyCycleAnalyzer.Analyze(context);

        var cycle = Assert.Single(context.ArchitectureComponentCycles);
        Assert.Equal(["package-a"], cycle.ComponentIds);
        Assert.True(cycle.ContainsSelfDependency);
        Assert.True(cycle.IsSingleNodeCycle);
        Assert.Single(cycle.ContributingRelationships);
    }

    [Fact]
    public void Keeps_production_and_test_cycles_separate()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureComponentDependencies =
            [
                Edge("a", "b", "Production", "prod-run", "prod-ab"),
                Edge("b", "a", "Production", "prod-run", "prod-ba"),
                Edge("a", "b", "Test", "test-run", "test-ab"),
                Edge("b", "a", "Test", "test-run", "test-ba")
            ]
        };

        ArchitectureDependencyCycleAnalyzer.Analyze(context);

        Assert.Equal(2, context.ArchitectureComponentCycles.Count);
        Assert.Contains(context.ArchitectureComponentCycles, cycle => cycle.Scope == "Production");
        Assert.Contains(context.ArchitectureComponentCycles, cycle => cycle.Scope == "Test");
        Assert.Equal(4, context.ArchitectureComponentCycles.Sum(cycle => cycle.RelationshipCount));
        Assert.All(context.ArchitectureComponentCycles, cycle => Assert.Equal(2, cycle.RelationshipCount));
    }

    [Fact]
    public void Detects_cycle_created_only_after_logical_component_aggregation()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureComponentDependencies =
            [
                Edge("code-a1", "code-b1", "Production", "run-1", "code-ab"),
                Edge("code-b2", "code-a2", "Production", "run-1", "code-ba")
            ],
            ArchitecturalComponentDependencies =
            [
                ArchitecturalEdge("logical-a", "logical-b", "Production", "run-1", "logical-ab"),
                ArchitecturalEdge("logical-b", "logical-a", "Production", "run-1", "logical-ba")
            ]
        };

        ArchitectureDependencyCycleAnalyzer.Analyze(context);

        Assert.Empty(context.ArchitectureComponentCycles);
        var logicalCycle = Assert.Single(context.ArchitecturalComponentCycles);
        Assert.Equal("ArchitecturalComponent", logicalCycle.Projection);
        Assert.Equal(["logical-a", "logical-b"], logicalCycle.ComponentIds);
    }

    private static ArchitectureComponentDependencyContext Edge(
        string source,
        string target,
        string scope,
        string runId,
        string relationshipId) =>
        new()
        {
            SourceComponentId = source,
            SourceComponentName = source,
            TargetComponentId = target,
            TargetComponentName = target,
            Scope = scope,
            RelationshipCount = 1,
            ContributingRelationships =
            [
                new SemanticRelationshipReference
                {
                    RunId = runId,
                    RelationshipId = relationshipId
                }
            ]
        };
    private static ArchitecturalComponentDependencyContext ArchitecturalEdge(
        string source,
        string target,
        string scope,
        string runId,
        string relationshipId) =>
        new()
        {
            SourceComponentId = source,
            SourceComponentName = source,
            TargetComponentId = target,
            TargetComponentName = target,
            Scope = scope,
            RelationshipCount = 1,
            ContributingRelationships =
            [
                new SemanticRelationshipReference
                {
                    RunId = runId,
                    RelationshipId = relationshipId
                }
            ]
        };

}
