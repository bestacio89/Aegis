namespace Aegis.Shared.Security.Live;

/// <summary>
/// How much a live probe is allowed to touch a target.
/// Passive: reads only what a service volunteers (TLS handshake, headers, published metadata).
/// Light:   bounded, rate-limited active checks (e.g. a single connect per port).
/// Intrusive techniques (exploitation, brute force, fuzzing) are deliberately not representable.
/// </summary>
public enum ProbeIntensity
{
    Passive = 0,
    Light = 1
}