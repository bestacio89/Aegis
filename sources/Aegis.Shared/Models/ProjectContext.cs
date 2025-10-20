using Aegis.Shared.Enums;

namespace Aegis.Shared.Models;

/// <summary>
/// 🧭 Describes the inferred architectural, technological, and structural profile of a project.
/// Generated deterministically by Aegis prior to evaluation.
/// </summary>
public sealed class ProjectContext
{
    // 🔖 Core Identity
    public string Language { get; set; } = "Unknown";           // e.g. "C#", "Python", "Java", "TypeScript"
    public string? Framework { get; set; }                      // e.g. ".NET 9", "Spring Boot", "Angular"
    public double Confidence { get; set; }                      // 0.0 → uncertain, 1.0 → confident inference
    public string RootPath { get; set; } = string.Empty;        // Root folder of the analyzed project

    // 🧩 Structural Classification
    public string? DomainType { get; set; }                     // "Frontend", "Backend", "Library", "Infra"
    public string? Layer { get; set; }                          // "Domain", "Application", "Infrastructure", "Api"
    public string? ArchitectureStyle { get; set; }              // "Clean", "CQRS", "MVC", "Hexagonal", etc.
    public string? Nature { get; set; }                         // "Service", "SDK", "Library", "TestSuite"
    public bool IsMultiModule { get; set; }                     // True if multiple modules/projects detected

    // ⚙️ Build / Dependency Metadata
    public BuildSystem BuildSystem { get; set; } = BuildSystem.Unknown;
    public string? EntryPointFile { get; set; }
    public List<string> DetectedDependencies { get; set; } = new();
    public List<string> DetectedFrameworks { get; set; } = new();

    // 🧱 Environment & Deployability
    public Dictionary<string, string> MetadataMap { get; set; } = new();
    public bool IsDockerized => MetadataMap.TryGetValue("Dockerized", out var d) && d == "true";
    public bool UsesKubernetes => MetadataMap.TryGetValue("Kubernetes", out var k) && k == "true";
    public bool IsDeployable => IsDockerized || UsesKubernetes;

    // 🧠 Detection Metadata
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public string DetectorVersion { get; set; } = "1.3.0";
    public string DetectionStrategy { get; set; } = "Hybrid";

    // 📊 Confidence Breakdown
    public Dictionary<string, double> ConfidenceMap { get; set; } = new();

    // ⚙️ Runtime or Build Environment
    public string? TargetRuntime { get; set; }
    public string? OperatingSystem { get; set; }

    // 📈 Statistical Insight
    public int FileCount { get; set; }
    public int LinesOfCode { get; set; }
    public double AverageComplexity { get; set; }

    // 📦 Static project identity snapshot
    public ProjectMetadata? Metadata { get; set; }

    // 🧾 Constructors
    public ProjectContext() { }

    public ProjectContext(string language, string? framework, double confidence)
    {
        Language = language;
        Framework = framework;
        Confidence = confidence;
    }

    // 🧠 Utility
    public override string ToString() =>
        $"{Language} - {Framework ?? "Unknown"} ({Confidence:P0}) " +
        $"[{DomainType ?? "Unclassified"} / {Layer ?? "Unknown"} / {ArchitectureStyle ?? "N/A"}]";

    // 🧩 Compact DTO for reports
    public object ToSummaryDto() => new
    {
        Metadata?.Name,
        Metadata?.Path,
        Language,
        Framework,
        DomainType,
        Layer,
        ArchitectureStyle,
        Nature,
        BuildSystem = BuildSystem.ToString(),
        Confidence,
        IsMultiModule,
        IsDeployable,
        EntryPointFile,
        DetectorVersion,
        DetectionStrategy,
        DetectedAt
    };
}
