using System;
using System.IO;
using System.Threading;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;
using Xunit;

namespace Aegis.Wpf.Tests;

public sealed class RelationshipLedgerViewModelTests
{
    [Fact]
    public void Update_UsesUnifiedGraphAsTheOnlySourceOfDetectedRelationships()
    {
        RunOnStaThread(() =>
        {
            var sourcePath = Path.Combine(Path.GetTempPath(), "aegis", "api");
            var targetPath = Path.Combine(Path.GetTempPath(), "aegis", "orders");
            var context = new ProjectArchitectureContext
            {
                ProjectName = "Ledger fixture",
                Dependencies =
                [
                    new ArchitectureDependencyContext
                    {
                        Source = "Legacy source",
                        Target = "Legacy target",
                        DependencyType = "LegacyDependency"
                    }
                ],
                ArchitectureGraphProjection = new ArchitectureGraphProjection(
                    [
                        new ArchitectureGraphNode
                        {
                            Id = "module:api",
                            Name = "Api",
                            Kind = ArchitectureGraphNodeKind.BuildModule,
                            ModulePath = sourcePath
                        },
                        new ArchitectureGraphNode
                        {
                            Id = "module:orders",
                            Name = "Orders",
                            Kind = ArchitectureGraphNodeKind.BuildModule,
                            ModulePath = targetPath
                        }
                    ],
                    [
                        new ArchitectureGraphEdge
                        {
                            Id = "graph-edge-1",
                            SourceNodeId = "module:api",
                            TargetNodeId = "module:orders",
                            Domain = ArchitectureGraphEdgeDomain.Build,
                            Scope = "Production",
                            ProviderRelationshipKind = "ProjectReference",
                            RelationshipKinds = [ArchitectureRelationshipKind.BuildProjectReference],
                            Evidence =
                            [
                                new ArchitectureGraphEvidenceReference
                                {
                                    Domain = ArchitectureGraphEvidenceDomain.BuildDependency,
                                    OccurrenceIndex = 4,
                                    EvidenceId = "build-evidence-4",
                                    ProviderId = "ProjectReference"
                                }
                            ]
                        }
                    ],
                    [])
            };

            var viewModel = new RelationshipLedgerViewModel();
            viewModel.Update(context);

            var relationship = Assert.Single(viewModel.Dependencies);
            Assert.Equal("graph-edge-1", relationship.GraphEdgeId);
            Assert.Equal("Build", relationship.GraphDomain);
            Assert.Equal("Api", relationship.Source);
            Assert.Equal("Orders", relationship.Target);
            Assert.Equal("Api → Orders", relationship.Direction);
            Assert.Equal("BuildProjectReference", relationship.DependencyType);
            Assert.Contains("BuildDependency[4]:build-evidence-4", relationship.EvidenceReferences);
            Assert.Equal(1, viewModel.TotalDependencies);
            Assert.DoesNotContain(viewModel.Dependencies, item => item.Source == "Legacy source");
        });
    }

    [Fact]
    public void Update_PresentsProviderKindWhenNormalizedRelationshipKindIsUnknown()
    {
        RunOnStaThread(() =>
        {
            var context = new ProjectArchitectureContext
            {
                ArchitectureGraphProjection = new ArchitectureGraphProjection(
                    [
                        new ArchitectureGraphNode
                        {
                            Id = "code:java-package:client",
                            Name = "sample.application",
                            Kind = ArchitectureGraphNodeKind.CodeComponent,
                            Language = "Java",
                            ComponentKind = "Package"
                        },
                        new ArchitectureGraphNode
                        {
                            Id = "code:java-package:domain",
                            Name = "sample.domain",
                            Kind = ArchitectureGraphNodeKind.CodeComponent,
                            Language = "Java",
                            ComponentKind = "Package"
                        }
                    ],
                    [
                        new ArchitectureGraphEdge
                        {
                            Id = "java-package-dependency",
                            SourceNodeId = "code:java-package:client",
                            TargetNodeId = "code:java-package:domain",
                            Domain = ArchitectureGraphEdgeDomain.NativeSemantic,
                            Scope = "Production",
                            ProviderRelationshipKind = "DependsOn",
                            RelationshipKinds = [ArchitectureRelationshipKind.Unknown],
                            Evidence =
                            [
                                new ArchitectureGraphEvidenceReference
                                {
                                    Domain = ArchitectureGraphEvidenceDomain.SemanticRelationship,
                                    OccurrenceIndex = 0,
                                    EvidenceId = "run-1:relationship-1",
                                    ProviderId = "relationship-1"
                                }
                            ]
                        }
                    ],
                    [])
            };

            var viewModel = new RelationshipLedgerViewModel();
            viewModel.Update(context);

            var relationship = Assert.Single(viewModel.Dependencies);
            Assert.Equal("DependsOn", relationship.DependencyType);
            Assert.Equal("DependsOn", relationship.ProviderRelationshipKind);
            Assert.Equal("Package", relationship.SourceLayer);
            Assert.Equal("Package", relationship.TargetLayer);
        });
    }

    [Fact]
    public void Update_KeepsUnprojectedEvidenceOutsideResolvedRelationshipRows()
    {
        RunOnStaThread(() =>
        {
            var context = new ProjectArchitectureContext
            {
                ArchitectureGraphProjection = new ArchitectureGraphProjection(
                    [
                        Node("module:client", "Client", ArchitectureGraphNodeKind.BuildModule),
                        Node("module:server", "Server", ArchitectureGraphNodeKind.BuildModule)
                    ],
                    [
                        new ArchitectureGraphEdge
                        {
                            Id = "resolved-edge",
                            SourceNodeId = "module:client",
                            TargetNodeId = "module:server",
                            Domain = ArchitectureGraphEdgeDomain.Api,
                            Scope = "Production",
                            RelationshipKinds = [ArchitectureRelationshipKind.HttpRequest],
                            Evidence =
                            [
                                new ArchitectureGraphEvidenceReference
                                {
                                    Domain = ArchitectureGraphEvidenceDomain.ApiClientRequest,
                                    OccurrenceIndex = 0,
                                    EvidenceId = "request-1",
                                    ProviderId = "CSharp.HttpClient"
                                },
                                new ArchitectureGraphEvidenceReference
                                {
                                    Domain = ArchitectureGraphEvidenceDomain.ApiEndpoint,
                                    OccurrenceIndex = 2,
                                    EvidenceId = "endpoint-1",
                                    ProviderId = "AspNetCore"
                                }
                            ]
                        }
                    ],
                    [
                        new ArchitectureGraphUnprojectedEvidence
                        {
                            Evidence = new ArchitectureGraphEvidenceReference
                            {
                                Domain = ArchitectureGraphEvidenceDomain.ApiClientRequest,
                                OccurrenceIndex = 1,
                                EvidenceId = "ambiguous-request",
                                ProviderId = "CSharp.HttpClient"
                            },
                            Reason = ArchitectureGraphUnprojectedReason.AmbiguousApiMatch,
                            Explanation = "More than one endpoint candidate remains."
                        }
                    ])
            };

            var viewModel = new RelationshipLedgerViewModel();
            viewModel.Update(context);

            Assert.Single(viewModel.Dependencies);
            var unprojected = Assert.Single(viewModel.UnprojectedEvidence);
            Assert.Equal(ArchitectureGraphUnprojectedReason.AmbiguousApiMatch, unprojected.Reason);
            Assert.Equal(1, viewModel.UnprojectedEvidenceCount);
            Assert.Contains("More than one endpoint candidate", unprojected.Explanation);
        });
    }

    [Fact]
    public void Update_KeepsUserDeclaredIntegrationsSeparateFromGraphEdges()
    {
        RunOnStaThread(() =>
        {
            var declaration = new ArchitectureIntegrationContext
            {
                SourceModuleName = "Client",
                SourceModulePath = Path.Combine(Path.GetTempPath(), "aegis", "client"),
                TargetModuleName = "Server",
                TargetModulePath = Path.Combine(Path.GetTempPath(), "aegis", "server"),
                InteractionPattern = "RequestResponse",
                Protocol = "HTTP",
                EvidenceOrigin = "UserDefined",
                Notes = "Declared by an architect."
            };
            var context = new ProjectArchitectureContext
            {
                ApplicationIntegrations = [declaration],
                ArchitectureGraphProjection = new ArchitectureGraphProjection([], [], [])
            };

            var viewModel = new RelationshipLedgerViewModel();
            viewModel.Update(context);

            Assert.Empty(viewModel.Dependencies);
            Assert.Same(declaration, Assert.Single(viewModel.ApplicationIntegrations));
            Assert.Empty(viewModel.UnprojectedEvidence);
        });
    }

    [Fact]
    public void Update_PreservesGraphScopeAndRelationshipDomainForFiltering()
    {
        RunOnStaThread(() =>
        {
            var context = new ProjectArchitectureContext
            {
                ArchitectureGraphProjection = new ArchitectureGraphProjection(
                    [
                        Node("module:production", "Production", ArchitectureGraphNodeKind.BuildModule),
                        Node("resource:broker", "Broker", ArchitectureGraphNodeKind.ExternalResource, "MessageDestination")
                    ],
                    [
                        new ArchitectureGraphEdge
                        {
                            Id = "test-message-edge",
                            SourceNodeId = "module:production",
                            TargetNodeId = "resource:broker",
                            Domain = ArchitectureGraphEdgeDomain.Messaging,
                            Scope = "Test",
                            RelationshipKinds = [ArchitectureRelationshipKind.MessagePublish]
                        }
                    ],
                    [])
            };

            var viewModel = new RelationshipLedgerViewModel();
            viewModel.Update(context);

            var relationship = Assert.Single(viewModel.Dependencies);
            Assert.Equal("Messaging", relationship.GraphDomain);
            Assert.Equal("Test", relationship.Scope);
            Assert.Equal("MessagePublish", relationship.DependencyType);

            viewModel.SelectedScope = "Production";

            Assert.Equal(0, viewModel.FilteredDependencies);
            Assert.Null(viewModel.SelectedDependency);
        });
    }

    private static ArchitectureGraphNode Node(
        string id,
        string name,
        ArchitectureGraphNodeKind kind,
        string? resourceKind = null) =>
        new()
        {
            Id = id,
            Name = name,
            Kind = kind,
            ResourceKind = resourceKind
        };

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new AggregateException("The WPF ViewModel test failed on its STA thread.", failure);
    }
}
