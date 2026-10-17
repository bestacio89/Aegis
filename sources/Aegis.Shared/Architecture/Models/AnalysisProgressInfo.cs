namespace Aegis.Shared.Architecture.Models;

/// <summary>One progress report from an analysis run (percent is 0-100).</summary>
public sealed record AnalysisProgressInfo(double Percent, string Stage, string? Detail = null);