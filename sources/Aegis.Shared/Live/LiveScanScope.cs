namespace Aegis.Shared.Security.Live;

/// <summary>
/// The recorded authorization for a live assessment. Nothing is probed unless a scope exists,
/// validates, and still covers the target at the moment of each probe.
/// </summary>
public sealed class LiveScanScope
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public IReadOnlyList<LiveTarget> Targets { get; init; } = Array.Empty<LiveTarget>();

    /// <summary>Start of the authorized testing window (UTC).</summary>
    public DateTimeOffset ValidFromUtc { get; init; }

    /// <summary>End of the authorized testing window (UTC).</summary>
    public DateTimeOffset ValidUntilUtc { get; init; }

    public ProbeIntensity MaxIntensity { get; init; } = ProbeIntensity.Passive;

    /// <summary>Reference to the written authorization (contract, annex, ticket number).</summary>
    public string AuthorizationReference { get; init; } = string.Empty;

    /// <summary>Who on the client side authorized the test.</summary>
    public string AuthorizedBy { get; init; } = string.Empty;

    /// <summary>When the assessor confirmed they hold the authorization. Null = not confirmed.</summary>
    public DateTimeOffset? ConsentGivenAtUtc { get; init; }
}