namespace Aegis.Shared.Architecture.Models.Policies.Infrastructure
{
    public class ConfigurationPolicy
    {
        public bool Enabled { get; set; } = true;
        public string[] RequiredFiles { get; set; } = ["appsettings.json", "application.yml", ".env", "package.json", "docker-compose.yml"];
        public bool EnforceEnvVariables { get; set; } = true;
        public string[] ForbiddenKeys { get; set; } = ["password", "secret", "apikey", "connectionstring", "token", "privatekey", "credential"];
        public bool DetectDuplicates { get; set; } = true;
        public bool EnforceKeyCasing { get; set; } = true;
        public bool ValidateSyntax { get; set; } = true;
        public bool CheckInfrastructureConfigs { get; set; } = true;
        public bool CrossLanguageScan { get; set; } = true;
        public long MaxFileSizeBytes { get; set; } = 2 * 1024 * 1024;

        // 👇 NEW: Python requirements governance
        public bool CheckPythonRequirements { get; set; } = true;
        public bool RequirePinnedVersions { get; set; } = true;           // e.g., foo==1.2.3 or ~= / >=
        public bool DisallowEditableInstalls { get; set; } = true;        // -e / --editable
        public bool WarnVcsOrUrlDeps { get; set; } = true;                // git+https://..., file:/, http://
        public string[] RequirementsFiles { get; set; } = ["requirements.txt", "requirements-dev.txt"];
    }
}
