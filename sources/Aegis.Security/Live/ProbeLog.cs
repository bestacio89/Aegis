using Aegis.Shared.Security.Live;
using System.Security.Cryptography;
using System.Text;

namespace Aegis.Security.Live;

/// <summary>Append-only, hash-chained probe log (SHA-256). Thread-safe.</summary>
public sealed class ProbeLog : IProbeLog
{
    private const string GenesisHash = "GENESIS";

    private readonly object _gate = new();
    private readonly List<ProbeLogEntry> _entries = new();
    private readonly Action<ProbeLogEntry>? _onRecorded;
    private readonly TimeProvider _clock;

    public ProbeLog(TimeProvider? clock = null, Action<ProbeLogEntry>? onRecorded = null)
    {
        _clock = clock ?? TimeProvider.System;
        _onRecorded = onRecorded;
    }

    public IReadOnlyList<ProbeLogEntry> Entries
    {
        get
        {
            lock (_gate)
                return _entries.ToList();
        }
    }

    public ProbeLogEntry Record(string probeId, string target, string action, string outcome, string? detail = null)
    {
        ProbeLogEntry entry;

        lock (_gate)
        {
            var sequence = _entries.Count + 1;
            var previous = _entries.Count == 0 ? GenesisHash : _entries[^1].Hash;
            var timestamp = _clock.GetUtcNow();

            var hash = ComputeHash(sequence, timestamp, probeId, target, action, outcome, detail, previous);

            entry = new ProbeLogEntry(sequence, timestamp, probeId, target, action, outcome, detail, previous, hash);
            _entries.Add(entry);
        }

        // Raised outside the lock so a slow listener can never block probing.
        _onRecorded?.Invoke(entry);
        return entry;
    }

    public bool VerifyIntegrity()
    {
        lock (_gate)
        {
            var previous = GenesisHash;

            foreach (var e in _entries)
            {
                if (!string.Equals(e.PreviousHash, previous, StringComparison.Ordinal))
                    return false;

                var expected = ComputeHash(e.Sequence, e.TimestampUtc, e.ProbeId, e.Target, e.Action, e.Outcome, e.Detail, e.PreviousHash);
                if (!string.Equals(e.Hash, expected, StringComparison.Ordinal))
                    return false;

                previous = e.Hash;
            }

            return true;
        }
    }

    private static string ComputeHash(
        long sequence,
        DateTimeOffset timestamp,
        string probeId,
        string target,
        string action,
        string outcome,
        string? detail,
        string previousHash)
    {
        var payload = $"{sequence}|{timestamp:O}|{probeId}|{target}|{action}|{outcome}|{detail}|{previousHash}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}