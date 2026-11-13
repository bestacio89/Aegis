using System.Text.Json;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class SemanticAnalysisContractTests
{
    [Fact]
    public void Analysis_run_distinguishes_all_execution_outcomes()
    {
        var statuses = Enum.GetValues<SemanticAnalysisStatus>();

        Assert.Contains(SemanticAnalysisStatus.NotRequested, statuses);
        Assert.Contains(SemanticAnalysisStatus.Unsupported, statuses);
        Assert.Contains(SemanticAnalysisStatus.Completed, statuses);
        Assert.Contains(SemanticAnalysisStatus.PartiallyCompleted, statuses);
        Assert.Contains(SemanticAnalysisStatus.Failed, statuses);
    }

    [Theory]
    [InlineData(SemanticAnalysisStatus.NotRequested, false)]
    [InlineData(SemanticAnalysisStatus.Unsupported, false)]
    [InlineData(SemanticAnalysisStatus.Completed, true)]
    [InlineData(SemanticAnalysisStatus.PartiallyCompleted, false)]
    [InlineData(SemanticAnalysisStatus.Failed, false)]
    public void Analysis_run_is_complete_only_for_completed_status(
        SemanticAnalysisStatus status,
        bool expected)
    {
        var run = new SemanticAnalysisRun
        {
            Language = "Java",
            EngineId = "fixture-engine",
            ProjectPath = "/workspace/sample",
            Status = status
        };

        Assert.Equal(expected, run.IsComplete);
    }

    [Fact]
    public void Unresolved_relationship_preserves_observation_without_fabricating_target_identity()
    {
        var relationship = new SemanticRelationship
        {
            Id = "java:sample.Service#execute()->external.MissingService",
            SourceSymbolId = "java:sample.Service",
            UnresolvedTarget = "external.MissingService",
            Kind = SemanticRelationshipKind.DependsOn,
            ResolutionStatus = SemanticResolutionStatus.Unresolved,
            Provenance =
            [
                new SemanticEvidenceProvenance
                {
                    AnalyzerId = "fixture.java",
                    Source = SemanticEvidenceSource.Parser,
                    FilePath = "src/main/java/sample/Service.java",
                    StartLine = 18,
                    StartColumn = 9,
                    Confidence = 0.75
                }
            ]
        };

        Assert.Null(relationship.TargetSymbolId);
        Assert.Equal("external.MissingService", relationship.UnresolvedTarget);
        Assert.Equal(SemanticResolutionStatus.Unresolved, relationship.ResolutionStatus);
        Assert.Equal(18, Assert.Single(relationship.Provenance).StartLine);
    }

    [Fact]
    public void Project_context_starts_without_claiming_semantic_analysis_completed()
    {
        var context = new ProjectArchitectureContext();

        Assert.Empty(context.SemanticAnalysisRuns);

        var summary = JsonSerializer.SerializeToElement(context.ToSummaryDto());

        Assert.Equal(0, summary.GetProperty("SemanticAnalysisRunCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("CompletedSemanticAnalysisRunCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("DependencyEvidenceCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("FrameworkEvidenceCount").GetInt32());
    }

    [Fact]
    public void Project_context_retains_dependency_and_framework_evidence_with_provenance()
    {
        var context = new ProjectArchitectureContext();
        var provenance = new SemanticEvidenceProvenance
        {
            AnalyzerId = "fixture.maven",
            Source = SemanticEvidenceSource.BuildModel,
            FilePath = "pom.xml",
            StartLine = 24,
            Confidence = 1
        };

        context.DependencyEvidence.Add(new ArchitectureDependencyEvidence
        {
            Id = "maven:org.example:sample-test",
            Name = "sample-test",
            Ecosystem = "Maven",
            Kind = DependencyEvidenceKind.Package,
            DeclaredVersion = "2.1.0",
            ResolutionStatus = DependencyResolutionStatus.Declared,
            Scope = "test",
            SourceProjectPath = "/workspace/sample",
            IsDirect = true,
            IsTestOnly = true,
            Provenance = [provenance]
        });

        context.FrameworkEvidence.Add(new FrameworkEvidence
        {
            Id = "junit:junit-jupiter",
            Name = "JUnit Jupiter",
            Version = "5.11.0",
            Kind = FrameworkEvidenceKind.Testing,
            ProjectPath = "/workspace/sample",
            IsTestOnly = true,
            Provenance = [provenance]
        });

        var dependency = Assert.Single(context.DependencyEvidence);
        var framework = Assert.Single(context.FrameworkEvidence);

        Assert.Equal(DependencyResolutionStatus.Declared, dependency.ResolutionStatus);
        Assert.Null(dependency.ResolvedVersion);
        Assert.True(dependency.IsTestOnly);
        Assert.Equal(FrameworkEvidenceKind.Testing, framework.Kind);
        Assert.True(framework.IsTestOnly);
        Assert.Equal("fixture.maven", Assert.Single(framework.Provenance).AnalyzerId);
    }
}
