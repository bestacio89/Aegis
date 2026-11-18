using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// Presents unprojected graph evidence as a diagnostic surface without changing
/// graph eligibility or creating relationships.
/// </summary>
public sealed partial class UnprojectedEvidenceViewModel : ObservableObject
{
    private ICollectionView _evidenceView;

    public UnprojectedEvidenceViewModel()
    {
        ReasonFilters = ["All", .. Enum.GetNames<ArchitectureGraphUnprojectedReason>()];
        DomainFilters = ["All"];
        _evidenceView = CollectionViewSource.GetDefaultView(Evidence);
        _evidenceView.Filter = MatchesFilter;
    }

    public ObservableCollection<ArchitectureGraphUnprojectedEvidence> Evidence { get; } = new();

    public ICollectionView EvidenceView => _evidenceView;

    public IReadOnlyList<string> ReasonFilters { get; }

    public IReadOnlyList<string> DomainFilters { get; private set; }

    [ObservableProperty] private string projectName = "No assessment loaded";
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string selectedReason = "All";
    [ObservableProperty] private string selectedDomain = "All";
    [ObservableProperty] private ArchitectureGraphUnprojectedEvidence? selectedEvidence;
    [ObservableProperty] private int totalObservations;
    [ObservableProperty] private int filteredObservations;
    [ObservableProperty] private string reasonSummary = "No unprojected evidence.";
    [ObservableProperty] private string domainSummary = "No evidence domains.";

    public string SelectedEvidenceGuidance =>
        SelectedEvidence is null
            ? "Select an observation to inspect why it was retained outside the graph and what evidence is needed next."
            : GetGuidance(SelectedEvidence.Reason);

    public string SelectedEvidenceReference =>
        SelectedEvidence is null
            ? "No observation selected."
            : $"{SelectedEvidence.Evidence.Domain}[{SelectedEvidence.Evidence.OccurrenceIndex}]"
              + (string.IsNullOrWhiteSpace(SelectedEvidence.Evidence.EvidenceId)
                  ? string.Empty
                  : $" — {SelectedEvidence.Evidence.EvidenceId}")
              + (string.IsNullOrWhiteSpace(SelectedEvidence.Evidence.ProviderId)
                  ? string.Empty
                  : $" ({SelectedEvidence.Evidence.ProviderId})");

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedReasonChanged(string value) => RefreshFilter();

    partial void OnSelectedDomainChanged(string value) => RefreshFilter();

    partial void OnSelectedEvidenceChanged(ArchitectureGraphUnprojectedEvidence? value)
    {
        OnPropertyChanged(nameof(SelectedEvidenceGuidance));
        OnPropertyChanged(nameof(SelectedEvidenceReference));
    }

    public void Update(ProjectArchitectureContext? context)
    {
        ProjectName = string.IsNullOrWhiteSpace(context?.ProjectName)
            ? "No architecture context available"
            : context.ProjectName;

        SelectedEvidence = null;
        var observations = context?.ArchitectureGraphProjection?.UnprojectedEvidence
            ?? [];

        using (_evidenceView.DeferRefresh())
        {
            Evidence.Clear();
            foreach (var evidence in observations
                         .OrderBy(item => item.Evidence.Domain)
                         .ThenBy(item => item.Evidence.OccurrenceIndex)
                         .ThenBy(item => item.Reason))
            {
                Evidence.Add(evidence);
            }
        }

        TotalObservations = Evidence.Count;
        DomainFilters =
        [
            "All",
            .. Evidence.Select(item => item.Evidence.Domain.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(domain => domain, StringComparer.Ordinal)
        ];

        ReasonSummary = BuildSummary(Evidence.GroupBy(item => item.Reason.ToString())
            .Select(group => (group.Key, group.Count())));
        DomainSummary = BuildSummary(Evidence.GroupBy(item => item.Evidence.Domain.ToString())
            .Select(group => (group.Key, group.Count())));

        if (!ReasonFilters.Contains(SelectedReason, StringComparer.Ordinal))
            SelectedReason = "All";

        if (!DomainFilters.Contains(SelectedDomain, StringComparer.Ordinal))
            SelectedDomain = "All";

        OnPropertyChanged(nameof(EvidenceView));
        OnPropertyChanged(nameof(DomainFilters));
        OnPropertyChanged(nameof(SelectedEvidenceGuidance));
        OnPropertyChanged(nameof(SelectedEvidenceReference));
        RefreshFilter();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedReason = "All";
        SelectedDomain = "All";
        SelectedEvidence = null;
    }

    private bool MatchesFilter(object item)
    {
        if (item is not ArchitectureGraphUnprojectedEvidence evidence)
            return false;

        if (!SelectedReason.Equals("All", StringComparison.Ordinal)
            && !evidence.Reason.ToString().Equals(SelectedReason, StringComparison.Ordinal))
            return false;

        if (!SelectedDomain.Equals("All", StringComparison.Ordinal)
            && !evidence.Evidence.Domain.ToString().Equals(SelectedDomain, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var query = SearchText.Trim();
        return Contains(evidence.Evidence.EvidenceId, query)
            || Contains(evidence.Evidence.ProviderId, query)
            || Contains(evidence.Evidence.Domain.ToString(), query)
            || Contains(evidence.Evidence.OccurrenceIndex.ToString(), query)
            || Contains(evidence.Reason.ToString(), query)
            || Contains(evidence.Explanation, query);
    }

    private void RefreshFilter()
    {
        _evidenceView.Refresh();
        if (SelectedEvidence is not null && !_evidenceView.Contains(SelectedEvidence))
            SelectedEvidence = null;

        FilteredObservations = _evidenceView.Cast<object>().Count();
    }

    private static string BuildSummary(IEnumerable<(string Key, int Count)> entries)
    {
        var values = entries.OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}: {entry.Count:N0}")
            .ToArray();

        return values.Length == 0 ? "None" : string.Join("  •  ", values);
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static string GetGuidance(ArchitectureGraphUnprojectedReason reason) =>
        reason switch
        {
            ArchitectureGraphUnprojectedReason.MissingEndpoint =>
                "The observation does not identify a usable endpoint. Inspect the originating provider evidence and run-scoped symbol index; do not infer a target.",
            ArchitectureGraphUnprojectedReason.AmbiguousEndpointOwnership =>
                "More than one declaration may own the endpoint. Establish one explicit owner project path before projection; do not select an arbitrary declaration.",
            ArchitectureGraphUnprojectedReason.MissingResourceIdentity =>
                "The external resource has no stable identity. Supply provider-backed resource identity before it can become a graph node.",
            ArchitectureGraphUnprojectedReason.UnresolvedEvidence =>
                "The source observation remains unresolved. Inspect its original expression, resolution status, and provider diagnostics; retain it as unresolved until identity is established.",
            ArchitectureGraphUnprojectedReason.AmbiguousEvidenceReference =>
                "The evidence reference does not resolve to one exact registry occurrence. Verify the originating run index and relationship occurrence index; do not fall back to provider IDs alone.",
            ArchitectureGraphUnprojectedReason.UnsupportedRelationship =>
                "The relationship kind is not supported by the current projection contract. Extend normalization only when provider semantics are demonstrably equivalent; do not relabel the observation.",
            ArchitectureGraphUnprojectedReason.NoApiCandidate =>
                "No server endpoint candidate matched this client request. Inspect HTTP method, literal path evidence, and discovered endpoint coverage; do not manufacture an API edge.",
            ArchitectureGraphUnprojectedReason.AmbiguousApiMatch =>
                "Multiple server endpoint candidates remain. Resolve the ambiguity with stronger method or route evidence; do not choose the first candidate.",
            ArchitectureGraphUnprojectedReason.UnsupportedApiMatch =>
                "The candidate uses route or matching semantics not currently modelled. Extend the matcher with explicit tests before allowing projection.",
            ArchitectureGraphUnprojectedReason.InvalidApiEvidenceReference =>
                "The API evidence reference is invalid. Verify the request/endpoint registry occurrence and originating evidence domain before projection.",
            _ => "Inspect the original provider evidence and preserve the observation until its identity and semantics are established."
        };
}
