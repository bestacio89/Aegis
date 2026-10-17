namespace Aegis.Shared.Security.Live;

/// <summary>How strongly the observation supports the result.</summary>
public enum EvidenceConfidence
{
    /// <summary>Context only; not a claim about a weakness.</summary>
    Informational = 0,

    /// <summary>Consistent with a weakness, but the observation alone cannot prove it.</summary>
    Probable = 1,

    /// <summary>Directly observed (for example, the negotiated protocol in a completed handshake).</summary>
    Confirmed = 2
}