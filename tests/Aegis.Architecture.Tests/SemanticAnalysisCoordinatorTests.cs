using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class SemanticAnalysisCoordinatorTests
{
    [Fact]
    public async Task Selects_provider_by_language_and_requested_capabilities()
    {
        var csharp = new RecordingAdapter("roslyn", "C#", ["symbols"]);
        var java = new RecordingAdapter("eclipse-jdt", "Java", ["symbols", "relationships"]);
        var coordinator = new SemanticAnalysisCoordinator([csharp, java]);

        var run = await coordinator.AnalyzeAsync(CreateRequest("Java", ["symbols", "relationships"]));

        Assert.Equal("eclipse-jdt", run.EngineId);
        Assert.Equal(SemanticAnalysisStatus.Completed, run.Status);
        Assert.False(csharp.WasInvoked);
        Assert.True(java.WasInvoked);
    }

    [Fact]
    public async Task Reports_missing_language_provider_as_coverage_affecting_unsupported_run()
    {
        var coordinator = new SemanticAnalysisCoordinator(
            [new RecordingAdapter("roslyn", "C#", ["symbols"])]);

        var run = await coordinator.AnalyzeAsync(CreateRequest("Kotlin", ["symbols"]));

        Assert.Equal(SemanticAnalysisStatus.Unsupported, run.Status);
        Assert.Equal("SemanticProviderNotRegistered", Assert.Single(run.Diagnostics).Code);
        Assert.True(run.Diagnostics[0].AffectsCoverage);
    }

    [Fact]
    public async Task Does_not_select_provider_that_lacks_a_required_capability()
    {
        var coordinator = new SemanticAnalysisCoordinator(
            [new RecordingAdapter("typescript", "TypeScript", ["symbols"])]);

        var run = await coordinator.AnalyzeAsync(CreateRequest("TypeScript", ["symbols", "relationships"]));

        Assert.Equal(SemanticAnalysisStatus.Unsupported, run.Status);
        Assert.Equal("SemanticProviderCapabilitiesUnavailable", Assert.Single(run.Diagnostics).Code);
    }

    [Fact]
    public async Task Rejects_ambiguous_provider_selection_instead_of_choosing_registration_order()
    {
        var coordinator = new SemanticAnalysisCoordinator(
        [
            new RecordingAdapter("provider-a", "Python", ["symbols"]),
            new RecordingAdapter("provider-b", "Python", ["symbols"])
        ]);

        var run = await coordinator.AnalyzeAsync(CreateRequest("Python", ["symbols"]));

        Assert.Equal(SemanticAnalysisStatus.Unsupported, run.Status);
        Assert.Equal("SemanticProviderAmbiguous", Assert.Single(run.Diagnostics).Code);
    }

    private static SemanticAnalysisRequest CreateRequest(string language, IReadOnlyList<string> capabilities) =>
        new()
        {
            Language = language,
            ProjectPath = Path.GetTempPath(),
            SourceRoots = [Path.GetTempPath()],
            SourceFiles = [Path.Combine(Path.GetTempPath(), "Fixture.source")],
            RequestedCapabilities = capabilities
        };

    private sealed class RecordingAdapter(string adapterId, string language, IReadOnlyCollection<string> capabilities)
        : ISemanticAnalysisAdapter
    {
        public string AdapterId => adapterId;
        public string Language => language;
        public IReadOnlyCollection<string> SupportedCapabilities => capabilities;
        public bool WasInvoked { get; private set; }

        public bool CanAnalyze(SemanticAnalysisRequest request) =>
            request.Language.Equals(language, StringComparison.OrdinalIgnoreCase);

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            return ValueTask.FromResult(new SemanticAnalysisRun
            {
                RunId = "fixture-run",
                Language = language,
                EngineId = adapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                Capabilities = capabilities.ToList(),
                AnalyzedFileCount = request.SourceFiles.Count
            });
        }
    }
}
