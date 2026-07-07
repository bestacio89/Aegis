using Aegis.Shared.Architecture.Enums;

namespace Aegis.App.Wpf.models;

public sealed record RuleDashboardItem(
    string RuleName,
    ArchitectureRuleCategory Category,
    ArchitectureRuleSeverity Severity,
    string Message,
    double Impact);