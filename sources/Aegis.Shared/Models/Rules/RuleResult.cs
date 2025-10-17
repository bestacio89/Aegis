using Aegis.Shared.Enums;

namespace Aegis.Shared.Models.Rules
{
    /// <summary>
    /// Represents the outcome of a rule evaluation on a specific file, class, or metric.
    /// </summary>
    public sealed class RuleResult
    {
        public RuleResult() { }

        public RuleResult(
            string ruleId,
            string ruleName,
            RuleCategory category,
            RuleSeverity severity,
            string? filePath,
            string? @namespace,
            string message,
            DateTimeOffset detectedAt)
        {
            RuleId = ruleId;
            RuleName = ruleName;
            Category = category.ToString();
            Severity = severity;
            FilePath = filePath ?? string.Empty;
            Namespace = @namespace;
            Message = message;
            Timestamp = detectedAt;
        }

        /// <summary>Unique rule identifier, e.g. "AEG-ARCH-001".</summary>
        public string RuleId { get; init; } = string.Empty;

        /// <summary>Readable rule name, e.g. "Circular Dependency Detected".</summary>
        public string RuleName { get; init; } = string.Empty;

        /// <summary>High-level rule domain, e.g. Architecture / Design / Dependency / Security.</summary>
        public string Category { get; init; } = string.Empty;

        /// <summary>Severity of the violation.</summary>
        public RuleSeverity Severity { get; init; }

        /// <summary>Path to the file or entity where the violation was found.</summary>
        public string FilePath { get; init; } = string.Empty;

        /// <summary>Optional namespace or type name context.</summary>
        public string? Namespace { get; init; }

        /// <summary>Human-readable message describing the issue.</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>UTC timestamp of detection.</summary>
        public DateTimeOffset Timestamp { get; init; }

        /// <summary>Convenience ToString for logging or reporting.</summary>
        public override string ToString() =>
            $"{RuleId} [{Severity}] in {FilePath}: {Message}";
    }
}
