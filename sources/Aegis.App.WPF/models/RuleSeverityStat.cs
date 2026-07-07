using Aegis.Shared.Architecture.Enums;

namespace Aegis.App.Wpf.models;

public sealed record RuleSeverityStat(
    ArchitectureRuleSeverity Severity,
    int Count);