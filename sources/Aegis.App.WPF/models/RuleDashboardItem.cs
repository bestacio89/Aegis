using Aegis.Shared.Architecture.Enums;

namespace Aegis.Wpf.models;

public sealed record RuleDashboardItem(
    string RuleName,
    ArchitectureRuleCategory Category,
    ArchitectureRuleSeverity Severity,    
    string Target,
    string Message,
    double Impact);