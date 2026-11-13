namespace Aegis.Shared.Architecture.Enums;

/// <summary>Identifies whether a symbol originates in project source or an external model.</summary>
public enum SemanticSymbolOrigin
{
    Unknown = 0,
    Source = 1,
    Generated = 2,
    External = 3
}
