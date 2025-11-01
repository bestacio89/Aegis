using System.Diagnostics;

namespace Aegis.Architecture.Diagnostics;

/// <summary>
/// Measures elapsed time for various scan phases.
/// </summary>
public sealed class PerformanceTracker : IDisposable
{
    private readonly string _phase;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly Action<string, TimeSpan> _callback;

    public PerformanceTracker(string phase, Action<string, TimeSpan> callback)
    {
        _phase = phase;
        _callback = callback;
    }

    public void Dispose()
    {
        _sw.Stop();
        _callback(_phase, _sw.Elapsed);
    }
}
