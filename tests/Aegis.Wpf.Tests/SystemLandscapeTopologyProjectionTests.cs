using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;
using FluentAssertions;
using Xunit;

namespace Aegis.Wpf.Tests;

public sealed class SystemLandscapeTopologyProjectionTests
{
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