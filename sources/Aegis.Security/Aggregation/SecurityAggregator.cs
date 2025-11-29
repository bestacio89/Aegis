// Aegis.Security/Aggregation/SecurityAggregator.cs
using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Models;
using Microsoft.Extensions.Logging;

namespace Aegis.Security.Aggregation;

/// <summary>
/// Production-grade aggregator that composes domain evaluations into a report
/// and raises real-time snapshots for dashboards. Stateless and side-effect free.
/// </summary>
public sealed class SecurityAggregator : ISecurityAggregator, IDisposable
{
    private readonly ILogger<SecurityAggregator> _logger;
    private readonly object _sync = new();
    private bool _disposed;

    public event EventHandler<SecurityAggregationEventArgs>? OnAggregationUpdated;

    public SecurityAggregator(ILogger<SecurityAggregator> logger)
    {
        _logger = logger;
        _logger.LogInformation("SecurityAggregator initialized");
    }

    /// <summary>
    /// Aggregates the provided domain evaluations into a full report and emits a live snapshot.
    /// </summary>
    public Task<AegisSecurityReport> AggregateAsync(IEnumerable<SecurityEvaluationResult> evaluations, CancellationToken cancellationToken = default)
    {
        var evalList = evaluations?.ToList() ?? new List<SecurityEvaluationResult>();

        // Group evaluations per domain and let SecurityDomainSummary compute totals/metrics
        var domainSummaries = evalList
          .GroupBy(e => e.Category)
          .Select(g => new SecurityDomainSummary
          {
              Category = g.Key,
              Evaluations = g.ToList(),
              AggregatedAtUtc = DateTimeOffset.UtcNow
          })
          .ToList();

        var projectName = evalList.FirstOrDefault()?.TargetContext ?? "AggregatedEvaluation";

        var summary = new SecurityScanSummary
        {
            ProjectName = projectName,
            DomainSummaries = domainSummaries,
            CompletedAtUtc = domainSummaries.Any()
            ? domainSummaries.Max(d => d.AggregatedAtUtc)
            : DateTimeOffset.UtcNow
        };

        var report = new AegisSecurityReport
        {
            ProjectName = summary.ProjectName,
            Summary = summary,
            CompletedAtUtc = summary.CompletedAtUtc
        };

        _logger.LogInformation(
          "Security aggregation complete — Domains={Domains}, Max={Max}, Risk={Risk}, Compliance={Compliance:F2}%",
          summary.DomainCount, summary.MaxSeverity, summary.RiskLevel, summary.GlobalComplianceRate);

        RaiseAggregationUpdated(summary);
        return Task.FromResult(report);
    }

    private void RaiseAggregationUpdated(SecurityScanSummary snapshot)
    {
        lock (_sync)
        {
            OnAggregationUpdated?.Invoke(this, new SecurityAggregationEventArgs(snapshot));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
        _logger.LogInformation("SecurityAggregator disposed");
    }
}
