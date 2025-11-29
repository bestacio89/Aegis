using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Diagnostics;

namespace Aegis.Security.Analysis.Probes;

public sealed class PortProbe : ISecurityProbe
{
    private readonly ILogger<PortProbe> _logger;
    public string Id => "net-port-probe";
    public string Name => "Network Port Availability Probe";

    public PortProbe(ILogger<PortProbe> logger) => _logger = logger;

    public async Task<IEnumerable<SecuritySignal>> ExecuteAsync(ProjectSecurityContext ctx, CancellationToken ct = default)
    {
        var results = new List<SecuritySignal>();

        foreach (var port in new[] { 22, 80, 443 })
        {
            try
            {
                using var tcp = new TcpClient();
                var sw = Stopwatch.StartNew();
                await tcp.ConnectAsync(ctx.RepositoryRoot, port);
                sw.Stop();

                _logger.LogInformation("Port {Port} open (elapsed {Elapsed}ms)", port, sw.ElapsedMilliseconds);

                results.Add(new SecuritySignal
                {
                    DetectorId = Id,
                    Category = "Network",
                    Severity = SecuritySeverity.Info,
                    Score = 0.2,
                    Evidence = $"Port {port} open, responded in {sw.ElapsedMilliseconds}ms"
                });
            }
            catch
            {
                _logger.LogWarning("Port {Port} closed or filtered", port);
            }
        }

        return results;
    }
}
