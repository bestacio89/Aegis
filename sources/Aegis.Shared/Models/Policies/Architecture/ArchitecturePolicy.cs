namespace Aegis.Shared.Models.Policies.Architecture
{
    public class ArchitecturePolicy
    {
        public bool EnforceLayerBoundaries { get; set; } = true;

        public Dictionary<string, string[]> AllowedDependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Api"] = new[] { "App", "Domain" },
            ["App"] = new[] { "Domain" },
            ["Domain"] = Array.Empty<string>(),
            ["Infrastructure"] = new[] { "Domain" }
        };

        public Dictionary<string, string[]> LayerHints { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Api"] = new[] { "Api", "Presentation", "Controllers", "Ui" },
            ["App"] = new[] { "Application", "Services", "Handlers" },
            ["Domain"] = new[] { "Domain", "Core", "Entities", "Models" },
            ["Infrastructure"] = new[] { "Infrastructure", "Infra", "Data", "Persistence" }
        };

        /// <summary>
        /// Defines how strictly architecture rules apply per language.
        /// 'Strict' = enforced, 'Advisory' = warning, 'Ignored' = not evaluated.
        /// </summary>
        public Dictionary<string, EnforcementMode> EnforcementByLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CSharp"] = EnforcementMode.Strict,
            ["Java"] = EnforcementMode.Advisory,
            ["Python"] = EnforcementMode.Advisory,
            ["TypeScript"] = EnforcementMode.Strict,
            ["JavaScript"] = EnforcementMode.Advisory
        };

        // 🧩 Nested Design Patterns Policy
        public DesignPatternPolicy DesignPatterns { get; set; } = new();
    }

    public enum EnforcementMode
    {
        Strict,
        Advisory,
        Ignored
    }
   


}
