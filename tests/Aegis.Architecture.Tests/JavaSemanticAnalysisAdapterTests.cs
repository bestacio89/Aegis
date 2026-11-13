using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JavaSemanticAnalysisAdapterTests
{
    [Fact]
    public async Task Returns_unsupported_when_the_private_worker_is_not_deployed()
    {
        var adapter = new JavaSemanticAnalysisAdapter(new FakeJavaWorkerClient(false));
        var request = CreateRequest();

        var run = await adapter.AnalyzeAsync(request);

        Assert.Equal(SemanticAnalysisStatus.Unsupported, run.Status);
        Assert.Equal("eclipse-jdt", run.EngineId);
        Assert.Equal("JavaWorkerNotConfigured", Assert.Single(run.Diagnostics).Code);
        Assert.True(run.Diagnostics[0].AffectsCoverage);
    }

    [Fact]
    public async Task Marks_source_only_analysis_partial_when_external_dependencies_are_declared()
    {
        var adapter = new JavaSemanticAnalysisAdapter(new FakeJavaWorkerClient(true));
        var request = new SemanticAnalysisRequest
        {
            Language = "Java",
            ProjectPath = Path.GetTempPath(),
            SourceRoots = [Path.GetTempPath()],
            SourceFiles = [Path.Combine(Path.GetTempPath(), "Service.java")],
            HasDeclaredDependencies = true,
            ClasspathResolutionComplete = false
        };

        var run = await adapter.AnalyzeAsync(request);

        Assert.Equal(SemanticAnalysisStatus.PartiallyCompleted, run.Status);
        Assert.Contains(run.Diagnostics, diagnostic =>
            diagnostic.Code == "JavaExternalClasspathUnavailable"
            && diagnostic.AffectsCoverage);
    }

    private static SemanticAnalysisRequest CreateRequest() =>
        new()
        {
            Language = "Java",
            ProjectPath = Path.GetTempPath(),
            SourceRoots = [Path.GetTempPath()],
            SourceFiles = [Path.Combine(Path.GetTempPath(), "Service.java")]
        };

    private sealed class FakeJavaWorkerClient(bool isConfigured) : IJavaWorkerClient
    {
        public bool IsConfigured => isConfigured;

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SemanticAnalysisRun
            {
                RunId = "fixture-run",
                Language = "Java",
                EngineId = "eclipse-jdt",
                EngineVersion = "3.47.0",
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = 1
            });
    }
}
