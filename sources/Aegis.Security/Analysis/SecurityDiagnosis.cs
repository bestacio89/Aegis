using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Aegis.Security.Diagnostics;

public static class SecurityDiagnostics
{
    public static IDisposable TraceScope(ILogger logger, string operation)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("▶ Begin {Op}", operation);

        return new DisposableAction(() =>
        {
            sw.Stop();
            logger.LogInformation("■ End {Op} ({Ms} ms)", operation, sw.ElapsedMilliseconds);
        });
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _onDispose;
        public DisposableAction(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
