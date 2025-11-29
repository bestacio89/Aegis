using Aegis.Shared.Security.Enums;

namespace Aegis.Shared.Security.Models
{
    public sealed record SecurityFinding
    (
        SecuritySeverity SeverityLevel,
        string Message,
        string? Remediation = null,
        string? SourceSignal = null,
        string? Evaluator = null,
        IReadOnlyDictionary<string, object>? Metadata = null
    );
}
