// Aegis.Shared/Security/Models/Policies/AegisSecurityPolicy.cs
using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models.Policies;

public abstract class AegisSecurityPolicy
{
    /// <summary>Domain key (e.g., "Application", "Network").</summary>
    public abstract string Domain { get; }

    /// <summary>RuleSet keys this policy governs (must match your SecurityRuleSet.Keys).</summary>
    public abstract IReadOnlyCollection<string> RuleSetKeys { get; }

    /// <summary>Risk tolerance (0..1). Lower means stricter.</summary>
    public abstract double DefaultRiskTolerance { get; }

    /// <summary>Hard thresholds per severity. Enforced by evaluators.</summary>
    public abstract IReadOnlyDictionary<SecuritySeverity, double> SeverityThresholds { get; }

    /// <summary>Security is never optional.</summary>
    public virtual bool CanSkip => false;

    /// <summary>Always strict in Security.</summary>
    public virtual bool EnforceStrict => true;

    /// <summary>Centralized helper to decide if a score breaches policy for a given severity.</summary>
    public bool IsBreach(SecuritySeverity severity, double score)
    {
        if (!SeverityThresholds.TryGetValue(severity, out var min))
            return score > 0; // If not mapped, treat any >0 as breach.

        return score >= min;
    }

    public sealed override bool Equals(object? obj) =>
        obj is AegisSecurityPolicy other && other.Domain == Domain;

    public sealed override int GetHashCode() => Domain.GetHashCode();
}
