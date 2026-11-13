using Aegis.Shared.Architecture.Enums;

namespace Aegis.Shared.Architecture.Models;

/// <summary>
/// Records the classification of one language-level code container into one
/// logical architectural component without duplicating its semantic evidence.
/// </summary>
public sealed class ArchitecturalComponentMembership
{
    public string CodeComponentId { get; init; } = string.Empty;

    public string ArchitecturalComponentId { get; init; } = string.Empty;

    public ArchitectureClassificationSource ClassificationSource { get; init; }
        = ArchitectureClassificationSource.Unclassified;

    /// <summary>Confidence in the membership decision, bounded to [0, 1].</summary>
    public double Confidence { get; init; }

    public string Explanation { get; init; } = string.Empty;

    /// <summary>Explicit mapping or inference rule responsible for the decision.</summary>
    public string? ClassificationRuleId { get; init; }

    /// <summary>Matched package, namespace, module, or other provider-native identity.</summary>
    public string? MatchedIdentity { get; init; }
}
