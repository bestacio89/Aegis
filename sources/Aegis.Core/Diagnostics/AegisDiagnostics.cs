using System.Collections.Concurrent;
using Aegis.Shared.Diagnostics;

namespace Aegis.Core.Diagnostics;

public static class AegisDiagnostics
{
    private static readonly ConcurrentQueue<DiagnosticEvent> _events = new();
    public static event Action<DiagnosticEvent>? OnEvent;

    public static void Report(string source, DiagnosticLevel level, string message, Exception? ex = null)
    {
        var evt = new DiagnosticEvent(source, level, message, DateTime.UtcNow, ex);
        _events.Enqueue(evt);
        OnEvent?.Invoke(evt);
    }

    public static IEnumerable<DiagnosticEvent> GetHistory(int max = 100)
        => _events.Reverse().Take(max).ToArray();
}
