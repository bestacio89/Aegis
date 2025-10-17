public class ProjectContext
{
    public string Language { get; set; }
    public string? Framework { get; set; }
    public double Confidence { get; set; }

    // 🧱 Added metadata
    public string? Layer { get; set; }          // Domain, Application, Infrastructure, Api
    public string? Nature { get; set; }         // Framework, Service, SDK, etc.
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    public ProjectContext(string language, string? framework, double confidence)
    {
        Language = language;
        Framework = framework;
        Confidence = confidence;
    }

    public override string ToString() =>
        $"{Language} - {Framework} ({Confidence:P0}) [{Layer ?? "Unknown"} / {Nature ?? "Generic"}]";
}
