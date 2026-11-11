namespace Aegis.Shared.Architecture.Enums;

/// <summary>How a source root was established during static project discovery.</summary>
public enum ProjectSourceRootOrigin
{
    Conventional,
    Explicit,
    GeneratedConvention,
    Observed
}
