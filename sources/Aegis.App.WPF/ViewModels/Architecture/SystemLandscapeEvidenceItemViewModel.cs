using System.IO;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>Presentation-safe description of one exact architecture evidence occurrence.</summary>
public sealed class SystemLandscapeEvidenceItemViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Identity { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string SourceLocation { get; init; } = "Source location unavailable.";
    public string? VerifiedSourcePath { get; init; }
    public bool HasSourceLocation => !string.IsNullOrWhiteSpace(VerifiedSourcePath);
    public string AutomationName => $"{Title}. {Identity}";
    public string AutomationHelpText => $"{Details} {SourceLocation}";
    public string OpenSourceAutomationName => HasSourceLocation
        ? $"Open source file {Path.GetFileName(VerifiedSourcePath)}"
        : "Source file unavailable.";
}
