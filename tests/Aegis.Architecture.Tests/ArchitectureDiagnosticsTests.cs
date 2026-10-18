using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Diagnostics;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureDiagnosticsTests
{
    [Fact]
    public void Report_publishes_event_and_retains_it_in_history()
    {
        var source = $"ArchitectureDiagnosticsTests_{Guid.NewGuid():N}";
        var message = "diagnostic test event";
        DiagnosticEvent? observed = null;

        void Handler(DiagnosticEvent diagnostic)
        {
            if (diagnostic.Source == source)
                observed = diagnostic;
        }

        AegisDiagnostics.OnEvent += Handler;

        try
        {
            AegisDiagnostics.Report(source, DiagnosticLevel.Warning, message);

            Assert.NotNull(observed);
            Assert.Equal(source, observed!.Source);
            Assert.Equal(DiagnosticLevel.Warning, observed.Level);
            Assert.Equal(message, observed.Message);
            Assert.Contains(
                AegisDiagnostics.GetHistory(1000),
                diagnostic => diagnostic.Source == source && diagnostic.Message == message);
        }
        finally
        {
            AegisDiagnostics.OnEvent -= Handler;
        }
    }

    [Fact]
    public void PerformanceTracker_invokes_callback_once_when_disposed()
    {
        var callbacks = new List<(string Phase, TimeSpan Elapsed)>();

        var tracker = new PerformanceTracker(
            "architecture-evaluation",
            (phase, elapsed) => callbacks.Add((phase, elapsed)));

        tracker.Dispose();

        var callback = Assert.Single(callbacks);
        Assert.Equal("architecture-evaluation", callback.Phase);
        Assert.True(callback.Elapsed >= TimeSpan.Zero);
    }
}
