namespace Aegis.Shared.Security.Live;

/// <summary>
/// One line of the tamper-evident probe log. Each entry carries the hash of the previous one,
/// so removing or editing an entry afterwards breaks the chain.
/// </summary>
public sealed record ProbeLogEntry(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string ProbeId,
    string Target,
    string Action,
    string Outcome,
    string? Detail,
    string PreviousHash,
    string Hash)
{
    /// <summary>Single-line rendering for the UI log.</summary>
    public string Summary =>
        $"{TimestampUtc.ToLocalTime():HH:mm:ss}  {ProbeId,-14}  {Target,-24}  {Action,-18}  {Outcome}";
}