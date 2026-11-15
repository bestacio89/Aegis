namespace Aegis.Shared.Architecture.Enums;

/// <summary>Execution and ownership category for an Android test source set.</summary>
public enum GradleAndroidTestKind
{
    None,
    Unit,
    Instrumentation,
    Fixtures,
    Unknown
}
