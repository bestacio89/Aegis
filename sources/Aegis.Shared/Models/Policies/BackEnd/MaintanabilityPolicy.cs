namespace Aegis.Shared.Models.Policies.BackEnd;

/// <summary>
/// Governs maintainability requirements and documentation density thresholds.
/// </summary>
public sealed class MaintainabilityPolicy
{
    /// <summary>
    /// Minimum acceptable maintainability index (0-100 scale).
    /// Default: 70 (values below trigger warnings or errors).
    /// </summary>
    public int MinMaintainabilityIndex { get; set; } = 70;

    /// <summary>
    /// Whether to check for comment density in each source file.
    /// </summary>
    public bool RequireCommentDensityCheck { get; set; } = true;

    /// <summary>
    /// Minimum percentage of comments vs. total lines
    /// required when <see cref="RequireCommentDensityCheck"/> is true.
    /// Default: 5 %.
    /// </summary>
    public double MinCommentDensity { get; set; } = 5.0;

    /// <summary>
    /// Maximum allowed lines per file before additional penalties apply.
    /// Helps identify oversized, monolithic files.
    /// Default: 500.
    /// </summary>
    public int MaxLinesPerFile { get; set; } = 500;

    /// <summary>
    /// Weight factor applied to cyclomatic complexity in maintainability score.
    /// Allows tuning between complexity and size impact.
    /// Default: 2.
    /// </summary>
    public double ComplexityWeight { get; set; } = 2.0;

    /// <summary>
    /// Weight factor applied to file length when computing maintainability.
    /// Default: 0.02 (≈ 2 % penalty per 50 lines).
    /// </summary>
    public double LineWeight { get; set; } = 0.02;

    /// <summary>
    /// Optional flag to include documentation comment lines
    /// (/// or docstrings) in the maintainability score.
    /// </summary>
    public bool IncludeDocCommentsInScore { get; set; } = true;
}
