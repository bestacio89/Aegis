using Aegis.Shared.Architecture.Enums;

namespace Aegis.App.Wpf.models;

public sealed record RuleCategoryStat(
    ArchitectureRuleCategory Category,
    int Count);