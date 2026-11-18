using System;
using System.Threading;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;
using FluentAssertions;
using Xunit;

namespace Aegis.Wpf.Tests;

public sealed class UnprojectedEvidenceViewModelTests
{
    [Fact]
    public void Update_PreservesEveryObservationAndBuildsReasonAndDomainSummaries()
    {
        RunOnStaThread(() =>
        {
            var first = Evidence(ArchitectureGraphEvidenceDomain.SemanticRelationship, 3,
                ArchitectureGraphUnprojectedReason.MissingEndpoint, "missing endpoint");
            var second = Evidence(ArchitectureGraphEvidenceDomain.ApiClientRequest, 1,
                ArchitectureGraphUnprojectedReason.AmbiguousApiMatch, "ambiguous route");
            var third = Evidence(ArchitectureGraphEvidenceDomain.SemanticRelationship, 4,
                ArchitectureGraphUnprojectedReason.MissingEndpoint, "missing endpoint");

            var viewModel = new UnprojectedEvidenceViewModel();
            viewModel.Update(Context(first, second, third));

            viewModel.TotalObservations.Should().Be(3);
            viewModel.FilteredObservations.Should().Be(3);
            viewModel.Evidence.Should().Equal(second, first, third);
            viewModel.ReasonSummary.Should().Contain("MissingEndpoint: 2");
            viewModel.ReasonSummary.Should().Contain("AmbiguousApiMatch: 1");
            viewModel.DomainSummary.Should().Contain("SemanticRelationship: 2");
            viewModel.DomainSummary.Should().Contain("ApiClientRequest: 1");
        });
    }

    [Fact]
    public void Filters_ApplyReasonDomainAndFreeTextWithoutDiscardingSourceEvidence()
    {
        RunOnStaThread(() =>
        {
            var first = Evidence(ArchitectureGraphEvidenceDomain.SemanticRelationship, 3,
                ArchitectureGraphUnprojectedReason.MissingEndpoint, "missing endpoint");
            var second = Evidence(ArchitectureGraphEvidenceDomain.ApiClientRequest, 1,
                ArchitectureGraphUnprojectedReason.AmbiguousApiMatch, "ambiguous route");

            var viewModel = new UnprojectedEvidenceViewModel();
            viewModel.Update(Context(first, second));

            viewModel.SelectedReason = nameof(ArchitectureGraphUnprojectedReason.MissingEndpoint);
            viewModel.SelectedDomain = nameof(ArchitectureGraphEvidenceDomain.SemanticRelationship);
            viewModel.SearchText = "missing endpoint";

            viewModel.FilteredObservations.Should().Be(1);
            viewModel.Evidence.Should().HaveCount(2);
            viewModel.SelectedReason = "All";
            viewModel.SelectedDomain = "All";
            viewModel.SearchText = string.Empty;
            viewModel.FilteredObservations.Should().Be(2);
        });
    }

    [Theory]
    [InlineData(ArchitectureGraphUnprojectedReason.MissingEndpoint, "symbol index")]
    [InlineData(ArchitectureGraphUnprojectedReason.AmbiguousEndpointOwnership, "owner project path")]
    [InlineData(ArchitectureGraphUnprojectedReason.MissingResourceIdentity, "stable identity")]
    [InlineData(ArchitectureGraphUnprojectedReason.UnresolvedEvidence, "resolution status")]
    [InlineData(ArchitectureGraphUnprojectedReason.AmbiguousEvidenceReference, "occurrence index")]
    [InlineData(ArchitectureGraphUnprojectedReason.UnsupportedRelationship, "semantics")]
    [InlineData(ArchitectureGraphUnprojectedReason.NoApiCandidate, "HTTP method")]
    [InlineData(ArchitectureGraphUnprojectedReason.AmbiguousApiMatch, "Multiple server endpoint candidates")]
    [InlineData(ArchitectureGraphUnprojectedReason.UnsupportedApiMatch, "matcher")]
    [InlineData(ArchitectureGraphUnprojectedReason.InvalidApiEvidenceReference, "registry occurrence")]
    public void Selection_ProvidesReasonSpecificDiagnosticGuidance(
        ArchitectureGraphUnprojectedReason reason,
        string expectedGuidance)
    {
        RunOnStaThread(() =>
        {
            var evidence = Evidence(ArchitectureGraphEvidenceDomain.SemanticRelationship, 0, reason, "provider explanation");
            var viewModel = new UnprojectedEvidenceViewModel();
            viewModel.Update(Context(evidence));
            viewModel.SelectedEvidence = evidence;

            viewModel.SelectedEvidenceGuidance.Should().Contain(expectedGuidance);
            viewModel.SelectedEvidenceReference.Should().Contain("[0]");
        });
    }

    [Fact]
    public void Update_WithNullContextClearsPreviousEvidenceAndSelection()
    {
        RunOnStaThread(() =>
        {
            var viewModel = new UnprojectedEvidenceViewModel();
            var evidence = Evidence(ArchitectureGraphEvidenceDomain.SemanticRelationship, 0,
                ArchitectureGraphUnprojectedReason.MissingEndpoint, "missing endpoint");
            viewModel.Update(Context(evidence));
            viewModel.SelectedEvidence = evidence;

            viewModel.Update(null);

            viewModel.TotalObservations.Should().Be(0);
            viewModel.FilteredObservations.Should().Be(0);
            viewModel.Evidence.Should().BeEmpty();
            viewModel.SelectedEvidence.Should().BeNull();
            viewModel.ReasonSummary.Should().Be("None");
            viewModel.DomainSummary.Should().Be("None");
        });
    }

    private static ArchitectureGraphUnprojectedEvidence Evidence(
        ArchitectureGraphEvidenceDomain domain,
        int occurrence,
        ArchitectureGraphUnprojectedReason reason,
        string explanation) =>
        new()
        {
            Evidence = new ArchitectureGraphEvidenceReference
            {
                Domain = domain,
                OccurrenceIndex = occurrence,
                EvidenceId = $"evidence-{occurrence}",
                ProviderId = "test-provider"
            },
            Reason = reason,
            Explanation = explanation
        };

    private static ProjectArchitectureContext Context(params ArchitectureGraphUnprojectedEvidence[] evidence) =>
        new()
        {
            ProjectName = "Evidence fixture",
            ArchitectureGraphProjection = new ArchitectureGraphProjection([], [], evidence)
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
