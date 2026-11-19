using Aegis.Architecture.Analysis.Detectors;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class SemanticArchitectureProjectionDiagnosticsTests
{
    [Fact]
    public void Analyze_records_relationships_lost_at_each_projection_boundary_without_changing_graph_output()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), $"aegis-projection-diagnostics-{Guid.NewGuid():N}");
        var sourceFile = Path.Combine(projectPath, "application", "OrderService.java");
        var targetFile = Path.Combine(projectPath, "domain", "Order.java");
        var run = new SemanticAnalysisRun
        {
            RunId = "java-projection-run",
            Language = "Java",
            EngineId = "jdt-test-provider",
            ProjectPath = projectPath,
            Status = SemanticAnalysisStatus.Completed,
            Symbols =
            [
                new SemanticSymbol
                {
                    Id = "order-service",
                    Name = "OrderService",
                    QualifiedName = "sample.application.OrderService",
                    ContainerName = "sample.application",
                    ContainerKind = "Package",
                    Language = "Java",
                    ProjectPath = projectPath,
                    FilePath = sourceFile,
                    Origin = SemanticSymbolOrigin.Source
                },
                new SemanticSymbol
                {
                    Id = "order",
                    Name = "Order",
                    QualifiedName = "sample.domain.Order",
                    ContainerName = "sample.domain",
                    ContainerKind = "Package",
                    Language = "Java",
                    ProjectPath = projectPath,
                    FilePath = targetFile,
                    Origin = SemanticSymbolOrigin.Source
                }
            ],
            Relationships =
            [
                new SemanticRelationship
                {
                    Id = "resolved-reference",
                    SourceSymbolId = "order-service",
                    TargetSymbolId = "order",
                    Kind = SemanticRelationshipKind.References,
                    ResolutionStatus = SemanticResolutionStatus.Resolved
                },
                new SemanticRelationship
                {
                    Id = "resolved-reference",
                    SourceSymbolId = "order-service",
                    TargetSymbolId = "order",
                    Kind = SemanticRelationshipKind.References,
                    ResolutionStatus = SemanticResolutionStatus.Resolved
                },
                new SemanticRelationship
                {
                    Id = "unresolved-reference",
                    SourceSymbolId = "order-service",
                    UnresolvedTarget = "sample.external.MissingType",
                    Kind = SemanticRelationshipKind.References,
                    ResolutionStatus = SemanticResolutionStatus.Unresolved
                },
                new SemanticRelationship
                {
                    Id = "missing-target-reference",
                    SourceSymbolId = "order-service",
                    TargetSymbolId = "missing-target",
                    Kind = SemanticRelationshipKind.Calls,
                    ResolutionStatus = SemanticResolutionStatus.Resolved
                }
            ]
        };
        var context = new ProjectArchitectureContext
        {
            ProjectName = "projection-diagnostics",
            RootPath = projectPath,
            Language = "Java",
            Modules =
            [
                new ProjectModuleContext
                {
                    Name = "application",
                    Path = projectPath,
                    Languages = ["Java"]
                }
            ],
            SemanticAnalysisRuns = [run]
        };

        SemanticArchitectureComponentDetector.Analyze(context);

        Assert.Equal(2, context.ProductionComponents.Count);
        var edge = Assert.Single(context.ProductionComponentDependencies);
        Assert.Equal(2, edge.RelationshipCount);
        Assert.Equal([0, 1], edge.ContributingRelationships.Select(item => item.RelationshipIndex).ToArray());

        var diagnostic = Assert.Single(run.Diagnostics, item =>
            item.Code == "SemanticArchitectureComponentProjectionBaseline");
        Assert.Equal("Java", diagnostic.Language);
        Assert.Equal("jdt-test-provider", diagnostic.EngineId);
        Assert.Equal("java-projection-run", diagnostic.RunId);
        Assert.False(diagnostic.AffectsCoverage);
        Assert.Contains("Observed relationships=4", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("resolved candidates=3", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("unresolved or targetless observations=1", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("unresolved or ambiguous targets=1", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("projected relationships=2", diagnostic.Message, StringComparison.Ordinal);
    }
}
