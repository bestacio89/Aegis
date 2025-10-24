using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models;

/// <summary>
/// Summary for a logical "domain" (layer/module/service) within the project.
/// </summary>
public sealed class SecurityDomainSummary
{
    /// <summary>Domain key (e.g., "API", "Infrastructure", "Payments").</summary>
    public string Domain { get; init; } = string.Empty;

    public int Critical { get; init; }
    public int High { get; init; }
    public int Medium { get; init; }
    public int Low { get; init; }
    public int Info { get; init; }

    /// <summary>Optional: classify this domain primarily by category.</summary>
    public SecurityCategory? PrimaryCategory { get; init; }

    public int Total => Critical + High + Medium + Low + Info;
}
