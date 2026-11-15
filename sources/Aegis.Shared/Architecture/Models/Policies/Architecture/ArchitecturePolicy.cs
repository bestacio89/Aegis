namespace Aegis.Shared.Architecture.Models.Policies.Architecture
{
    public class ArchitecturePolicy
    {
        public bool EnforceLayerBoundaries { get; set; } = true;

        /// <summary>
        /// Default project-reference policy for a conventional layered/Clean
        /// Architecture solution. Project roles are normalized by LayerDetector.
        /// Teams can replace these defaults with their own governed boundaries.
        /// </summary>
        public Dictionary<string, string[]> AllowedDependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Api"] = ["Application", "Domain", "Infrastructure", "Contracts", "Shared", "Library"],
            ["Application"] = ["Domain", "Contracts", "Shared", "Library"],
            ["Domain"] = ["Shared", "Library"],
            ["Infrastructure"] = ["Domain", "Application", "Contracts", "Shared", "Library"],
            ["Contracts"] = ["Shared", "Library"],
            ["Presentation"] = ["Application", "Contracts", "Client", "Shared", "Library"],
            ["Consumer"] = ["Application", "Domain", "Infrastructure", "Contracts", "Shared", "Library"],
            ["Client"] = ["Contracts", "Shared", "Library"],
            ["Shared"] = ["Library"],
            ["Tests"] = ["Api", "Application", "Domain", "Infrastructure", "Contracts", "Presentation", "Consumer", "Client", "Shared", "Library", "Tests"]
        };

        public Dictionary<string, string[]> LayerHints { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Api"] = ["Api", "Controllers", "CompositionRoot"],
            ["Application"] = ["Application", "Services", "Handlers", "UseCases"],
            ["Domain"] = ["Domain", "Core", "Entities", "Models"],
            ["Infrastructure"] = ["Infrastructure", "Infra", "Data", "Persistence", "Repository"],
            ["Contracts"] = ["Contracts", "Abstractions", "Dtos", "Interfaces"],
            ["Presentation"] = ["Admin", "Presentation", "Web", "Ui", "Front"],
            ["Consumer"] = ["Consumer", "Worker", "Background", "Jobs"],
            ["Client"] = ["Client", "ClientHttp", "Unity"],
            ["Tests"] = ["Test", "Tests", "Testing"],
            ["Shared"] = ["Shared", "Common", "Commons"],
            ["Library"] = ["Library", "Libraries", "Lib"]
        };

        /// <summary>
        /// Defines how strictly architecture rules apply per language.
        /// 'Strict' = enforced, 'Advisory' = warning, 'Ignored' = not evaluated.
        /// </summary>
        public Dictionary<string, EnforcementMode> EnforcementByLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["C#"] = EnforcementMode.Strict,
            ["Java"] = EnforcementMode.Advisory,
            ["Kotlin"] = EnforcementMode.Advisory,
            ["Python"] = EnforcementMode.Advisory,
            ["TypeScript"] = EnforcementMode.Strict,
            ["JavaScript"] = EnforcementMode.Advisory
        };

        public DesignPatternPolicy DesignPatterns { get; set; } = new();
    }

    public enum EnforcementMode
    {
        Strict,
        Advisory,
        Ignored
    }
}
