namespace Aegis.Shared.Architecture.Enums;

/// <summary>Classifies a statically identified message destination without assuming broker semantics.</summary>
public enum MessageDestinationKind
{
    Unknown,
    Topic,
    Queue,
    Exchange,
    Channel
}
