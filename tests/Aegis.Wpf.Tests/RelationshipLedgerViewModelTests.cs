using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;

namespace Aegis.Wpf.Tests;

public sealed class RelationshipLedgerViewModelTests
{
    [Fact]
    public void Update_PreservesLogicalProvenanceAndCodeContainerDrillDown()
    {
        RunOnStaThread(() =>
        {
            var reference = new SemanticRelationshipReference
            {
                RunId = "run-42",
                RelationshipId = "relationship-17"
            };
            var contribution = new ArchitectureComponentDependencyContext
            {
                SourceComponentId = "container-api",
                SourceComponentName = "Example.Api",
                TargetComponentId = "container-domain",
                TargetComponentName = "Example.Domain",
                RelationshipCount = 3,
                ResolvedRelationshipCount = 2,
                PartiallyResolvedRelationshipCount = 1,
                ContributingRelationships = [reference],
                UnreferencedRelationshipCount = 1
            };
            var context = new ProjectArchitectureContext
            {
                ArchitectureComponents =
                [
                    new ArchitectureComponentContext
                    {
                        Id = "container-api",
                        Name = "Api",
                        QualifiedName = "Example.Api",
                        ComponentKind = "Namespace",
                        BuildModulePath = "src/Api"
                    },
                    new ArchitectureComponentContext
                    {
                        Id = "container-domain",
                        Name = "Domain",
                        QualifiedName = "Example.Domain",
                        ComponentKind = "Namespace",
                        BuildModulePath = "src/Domain"
                    }
                ],
                ArchitecturalComponents =
                [
                    new ArchitecturalComponentContext
                    {
                        Id = "logical-api",
                        Name = "API",
                        Role = "Presentation"
                    },
                    new ArchitecturalComponentContext
                    {
                        Id = "logical-domain",
                        Name = "Domain",
                        Role = "Domain"
                    }
                ],
                ArchitecturalComponentDependencies =
                [
                    new ArchitecturalComponentDependencyContext
                    {
                        SourceComponentId = "logical-api",
                        SourceComponentName = "API",
                        TargetComponentId = "logical-domain",
                        TargetComponentName = "Domain",
                        RelationshipCount = 3,
                        ResolvedRelationshipCount = 2,
                        PartiallyResolvedRelationshipCount = 1,
                        UnreferencedRelationshipCount = 1,
                        ContributingRelationships = [reference],
                        ContributingCodeContainerDependencies = [contribution]
                    }
                ]
            };

            var viewModel = new RelationshipLedgerViewModel();
            viewModel.Update(context);

            var logical = Assert.Single(viewModel.Dependencies.Where(
                record => record.RelationshipLevel == "Logical architecture"));

            Assert.Equal(3, logical.RelationshipCount);
            Assert.Equal(2, logical.ResolvedRelationshipCount);
            Assert.Equal(1, logical.PartiallyResolvedRelationshipCount);
            Assert.Equal(1, logical.UnreferencedRelationshipCount);
            Assert.Contains("run-42:relationship-17", logical.EvidenceReferences);
            Assert.Contains("Example.Api -> Example.Domain (3)", logical.CodeContainerContributions);
        });
    }

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
