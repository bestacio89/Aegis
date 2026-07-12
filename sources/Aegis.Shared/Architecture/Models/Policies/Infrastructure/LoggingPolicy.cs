namespace Aegis.Shared.Architecture.Models.Policies.Infrastructure
{
    /// <summary>
    /// Governs logging standards, frameworks, and message quality across multiple ecosystems.
    /// </summary>
    public class LoggingPolicy
    {
        public bool Enabled { get; set; } = true;

        // General conventions
        public bool RequireStructuredLogging { get; set; } = true;         // e.g. Serilog, Winston, loguru, SLF4J
        public bool DisallowConsoleOnlyLogging { get; set; } = true;       // ban plain Console.WriteLine/log()
        public bool RequireLogLevels { get; set; } = true;                 // info, warning, error, etc.
        public bool DetectHardcodedSecretsInLogs { get; set; } = true;     // prevent logging sensitive data

        // File requirements
        public string[] RequiredConfigFiles { get; set; } = ["appsettings.json", "serilog.json", "log4j2.xml"];
        public bool WarnInterpolatedStrings { get; set; } = true;
        // Minimum log level enforcement
        public string MinimumLevel { get; set; } = "Information";          // Info, Warn, Error, Debug

        // Language-specific expectations
        public Dictionary<string, string[]> FrameworkHints { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["C#"] = new[] { "Serilog", "Microsoft.Extensions.Logging", "NLog" },
            ["Java"] = new[] { "SLF4J", "Logback", "Log4J" },
            ["Python"] = new[] { "logging", "loguru" },
            ["Node"] = new[] { "winston", "pino", "bunyan" }
        };

        public bool CheckLoggingConfigurationUsage { get; set; } = true;
        public bool EnforceAsyncLogging { get; set; } = false;             // optional for performance
    }
}
