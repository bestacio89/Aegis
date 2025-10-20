namespace Aegis.Shared.Models.Policies.Dependency;

public sealed class DependencyPolicy
{
    /// <summary>Whether dependency hygiene evaluation is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Warn if outdated or vulnerable package versions exceed threshold.</summary>
    public bool CheckOutdatedPackages { get; set; } = true;

    /// <summary>Warn if unused references exceed threshold.</summary>
    public bool CheckUnusedReferences { get; set; } = true;

    /// <summary>Maximum allowed ratio of outdated packages (0–1 scale).</summary>
    public double MaxOutdatedPackageRatio { get; set; } = 0.15;

    /// <summary>Maximum number of unused references before triggering a warning.</summary>
    public int MaxUnusedReferences { get; set; } = 3;

    /// <summary>Minimum dependency freshness score (0–100).</summary>
    public double MinDependencyFreshness { get; set; } = 80;

    /// <summary>Maximum allowed version drift (major version difference tolerance).</summary>
    public int MaxMajorVersionDrift { get; set; } = 1;

    public int MaxDependencyDepth { get; set; } = 5;
}
