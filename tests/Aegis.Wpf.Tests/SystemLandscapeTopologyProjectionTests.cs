using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;
using FluentAssertions;
using Xunit;

namespace Aegis.Wpf.Tests;

public sealed class SystemLandscapeTopologyProjectionTests
{
    [Fact]
    public void DomainAndScopeFilters_RestrictInspectionListWithoutRemovingTopologyElements()
    {
        var first = new ArchitectureGraphEdge
        {
            Id = "data-access-edge", SourceNodeId = "code-component", TargetNodeId = "code-component",
            Domain = ArchitectureGraphEdgeDomain.DataAccess, Scope = "Production", RelationshipCount = 1
        };
        var second = new ArchitectureGraphEdge
        {
            Id = "native-edge", SourceNodeId = "code-component", TargetNodeId = "code-component",
            Domain = ArchitectureGraphEdgeDomain.NativeSemantic, Scope = "Test", RelationshipCount = 1
        };
        var context = new ProjectArchitectureContext
        {
            ArchitectureGraphProjection = new ArchitectureGraphProjection(
                [new ArchitectureGraphNode
                {
                    Id = "code-component", Name = "Orders.Repository", Kind = ArchitectureGraphNodeKind.CodeComponent,
                    ComponentKind = "Class", Language = "C#"
                }],
                [first, second], [])
        };
        var viewModel = new SystemLandscapeViewModel();
        viewModel.Update(context);
        viewModel.VisibleRelationships.Should().HaveCount(2);
        var nodeCount = viewModel.TopologyNodes.Count;

        viewModel.SelectedDomain = ArchitectureGraphEdgeDomain.DataAccess.ToString();
        viewModel.SelectedScope = "Production";

        viewModel.VisibleRelationships.Should().ContainSingle().Which.EdgeId.Should().Be("data-access-edge");
        viewModel.TopologyNodes.Should().HaveCount(nodeCount);
        viewModel.Relationships.Should().HaveCount(2);
        viewModel.ClearInteractionCommand.Execute(null);
        viewModel.VisibleRelationships.Should().HaveCount(2);
        viewModel.SelectedDomain.Should().Be("All");
        viewModel.SelectedScope.Should().Be("All");
    }

    [Fact]
    public void SelectingInternalRelationship_SelectsItsOwningContainerAndPreservesEvidence()
    {
        var edge = new ArchitectureGraphEdge
        {
            Id = "internal-edge", SourceNodeId = "code-component", TargetNodeId = "code-component",
            Domain = ArchitectureGraphEdgeDomain.DataAccess, Scope = "Production", RelationshipCount = 1
        };
        var context = new ProjectArchitectureContext
        {
            ArchitectureGraphProjection = new ArchitectureGraphProjection(
                [new ArchitectureGraphNode
                {
                    Id = "code-component", Name = "Orders.Repository", Kind = ArchitectureGraphNodeKind.CodeComponent,
                    ComponentKind = "Class", Language = "C#"
                }],
                [edge], [])
        };
        var viewModel = new SystemLandscapeViewModel();
        viewModel.Update(context);
        var relationship = viewModel.Relationships.Should().ContainSingle().Subject;

        viewModel.SelectRelationshipCommand.Execute(relationship);

        viewModel.SelectedRelationship.Should().BeSameAs(relationship);
        viewModel.SelectedTopologyNode.Should().BeSameAs(viewModel.TopologyNodes.Should().ContainSingle().Subject);
        viewModel.SelectedTopologyNode!.IsSelected.Should().BeTrue();
        viewModel.SelectionStatus.Should().Contain("DataAccess");
    }

    [Fact]
    public void SelectingRolledUpRelationship_HighlightsItsDirectedTopologyEdge()
    {
        var context = new ProjectArchitectureContext
        {
            ArchitectureGraphProjection = new ArchitectureGraphProjection(
                [
                    new ArchitectureGraphNode
                    {
                        Id = "module-a", Name = "A", Kind = ArchitectureGraphNodeKind.BuildModule,
                        ModulePath = "/repo/a", ComponentKind = "Service"
                    },
                    new ArchitectureGraphNode
                    {
                        Id = "module-b", Name = "B", Kind = ArchitectureGraphNodeKind.BuildModule,
                        ModulePath = "/repo/b", ComponentKind = "Service"
                    }
                ],
                [new ArchitectureGraphEdge
                {
                    Id = "project-reference", SourceNodeId = "module-a", TargetNodeId = "module-b",
                    Domain = ArchitectureGraphEdgeDomain.Build, Scope = "Production", RelationshipCount = 1,
                    RelationshipKinds = [ArchitectureRelationshipKind.BuildProjectReference]
                }],
                [])
        };
        var viewModel = new SystemLandscapeViewModel();
        viewModel.Update(context);
        var relationship = viewModel.Relationships.Should().ContainSingle().Subject;
        var topologyEdge = viewModel.TopologyEdges.Should().ContainSingle().Subject;

        viewModel.SelectRelationshipCommand.Execute(relationship);

        topologyEdge.IsSelected.Should().BeTrue();
        topologyEdge.IsHighlighted.Should().BeTrue();
        topologyEdge.StrokeThickness.Should().BeGreaterThan(1.4);
    }

    [Fact]
    public void Update_RetainsInternalLeafRelationshipIdentityAndEvidence()
    {
        var evidence = new ArchitectureGraphEvidenceReference
        {
            Domain = ArchitectureGraphEvidenceDomain.DataAccess,
            OccurrenceIndex = 7,
            EvidenceId = "evidence-7",
            ProviderId = "ef-core"
        };
        var edge = new ArchitectureGraphEdge
        {
            Id = "leaf-edge-7",
            SourceNodeId = "code-component",
            TargetNodeId = "code-component",
            Domain = ArchitectureGraphEdgeDomain.DataAccess,
            Scope = "Production",
            RelationshipCount = 1,
            Evidence = [evidence]
        };
        var context = new ProjectArchitectureContext
        {
            ProjectName = "Topology test",
            ArchitectureGraphProjection = new ArchitectureGraphProjection(
                [
                    new ArchitectureGraphNode
                    {
                        Id = "code-component",
                        Name = "Orders.Repository",
                        Kind = ArchitectureGraphNodeKind.CodeComponent,
                        ComponentKind = "Class",
                        Language = "C#"
                    }
                ],
                [edge],
                [])
        };

        var viewModel = new SystemLandscapeViewModel();
        viewModel.Update(context);

        var internalRelationship = viewModel.Relationships.Should().ContainSingle().Subject;
        internalRelationship.IsInternalToGroup.Should().BeTrue();
        internalRelationship.EdgeId.Should().Be("leaf-edge-7");
        internalRelationship.Evidence.Should().ContainSingle().Which.Should().BeSameAs(evidence);
        internalRelationship.ContributingLeafEdgeIds.Should().ContainSingle().Which.Should().Be("leaf-edge-7");
        viewModel.TopologyNodes.Should().ContainSingle(node => node.InternalRelationships.Count == 1);
    }
}
