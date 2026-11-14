using Aegis.Architecture.Analysis.Detectors;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinTestArchitectureTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Keeps_test_project_and_test_support_symbols_out_of_production_metrics(
        bool isTestProject,
        bool isTestSupport)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aegis-kotlin-test-architecture-{Guid.NewGuid():N}");
        var productionPath = Path.Combine(root, "service");
        var testPath = Path.Combine(root, "service-tests");
        var productionFile = Path.Combine(productionPath, "src", "main", "kotlin", "example", "Order.kt");
        var testFile = Path.Combine(testPath, "src", "main", "kotlin", "example", "OrderTests.kt");

        var productionModule = new ProjectModuleContext
        {
            Name = "service",
            Path = productionPath,
            Languages = ["Kotlin"]
        };
        var testModule = new ProjectModuleContext
        {
            Name = isTestProject ? "service-tests" : "service-testing",
            Path = testPath,
            Languages = ["Kotlin"],
            IsTestProject = isTestProject,
            IsTestSupport = isTestSupport
        };
        var productionSymbol = new SemanticSymbol
        {
            Id = "kotlin:production:Order",
            Name = "Order",
            QualifiedName = "example.Order",
            ContainerName = "example",
            ContainerKind = "Package",
            Language = "Kotlin",
            ProjectPath = productionPath,
            FilePath = productionFile,
            Origin = SemanticSymbolOrigin.Source
        };
        var testSymbol = new SemanticSymbol
        {
            Id = "kotlin:test:OrderTests",
            Name = "OrderTests",
            QualifiedName = "example.OrderTests",
            ContainerName = "example",
            ContainerKind = "Package",
            Language = "Kotlin",
            ProjectPath = testPath,
            FilePath = testFile,
            Origin = SemanticSymbolOrigin.Source,
            // Module ownership is authoritative even if a provider omits this flag.
            IsTestCode = false
        };
        var context = new ProjectArchitectureContext
        {
            ProjectName = "service",
            RootPath = root,
            Language = "Kotlin",
            Modules = [productionModule, testModule],
            SemanticAnalysisRuns =
            [
                new SemanticAnalysisRun
                {
                    RunId = "kotlin-production-run",
                    Language = "Kotlin",
                    EngineId = "kotlin-test-provider",
                    ProjectPath = productionPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols = [productionSymbol]
                },
                new SemanticAnalysisRun
                {
                    RunId = "kotlin-test-run",
                    Language = "Kotlin",
                    EngineId = "kotlin-test-provider",
                    ProjectPath = testPath,
                    Status = SemanticAnalysisStatus.Completed,
                    Symbols = [testSymbol],
                    Relationships =
                    [
                        new SemanticRelationship
                        {
                            Id = "kotlin:test-calls-production",
                            SourceSymbolId = testSymbol.Id,
                            TargetSymbolId = productionSymbol.Id,
                            Kind = SemanticRelationshipKind.Calls,
                            ResolutionStatus = SemanticResolutionStatus.Resolved
                        }
                    ]
                }
            ]
        };

        SemanticArchitectureComponentDetector.Analyze(context);

        var productionComponent = Assert.Single(context.ProductionComponents);
        Assert.Equal(1, productionComponent.ProductionSymbolCount);
        Assert.Equal(0, productionComponent.TestSymbolCount);

        var testComponent = Assert.Single(context.TestComponents);
        Assert.Equal(0, testComponent.ProductionSymbolCount);
        Assert.Equal(1, testComponent.TestSymbolCount);

        Assert.Empty(context.ProductionComponentDependencies);
        var testDependency = Assert.Single(context.TestComponentDependencies);
        Assert.Equal("Test", testDependency.Scope);
        Assert.Equal(1, testDependency.RelationshipCount);
        var relationship = Assert.Single(testDependency.ContributingRelationships);
        Assert.Equal("kotlin-test-run", relationship.RunId);
        Assert.Equal("kotlin:test-calls-production", relationship.RelationshipId);
    }
}
