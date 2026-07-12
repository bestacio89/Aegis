using Aegis.Shared.Architecture.Enums;

namespace Aegis.Wpf.Models;

public sealed record SectionDashboardItem(
    string SectionName,
    ArchitectureRuleCategory Category,
    double Score,
    int RuleCount,
    string Status,
    string Remarks);