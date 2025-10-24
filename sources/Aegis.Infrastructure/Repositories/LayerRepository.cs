using Aegis.Infrastructure.Data;
using Aegis.Shared.Architecture.Models;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Infrastructure.Repositories;

public sealed class DashboardRepository
{
    private readonly AegisDbContext _db;

    public DashboardRepository(AegisDbContext db) => _db = db;

    // 🧩 Aggregates results per layer
    public async Task<IEnumerable<ArchitectureSummaryItem>> GetLayerSummaryAsync(int reportId, CancellationToken token = default)
    {
        return await _db.RuleResults
            .Where(r => r.ReportId == reportId)
            .GroupBy(r => r.Domain)
            .Select(g => new ArchitectureSummaryItem
            {
                Key = g.Key ?? "Unknown",
                Count = g.Count()
            })
            .ToListAsync(token);
    }

    // 🧩 Aggregates results per rule category
    public async Task<IEnumerable<ArchitectureSummaryItem>> GetRuleCategorySummaryAsync(int reportId, CancellationToken token = default)
    {
        return await _db.RuleResults
            .Where(r => r.ReportId == reportId)
            .GroupBy(r => r.Category)
            .Select(g => new ArchitectureSummaryItem
            {
                Key = g.Key ?? "Unknown",
                Count = g.Count()
            })
            .ToListAsync(token);
    }

    // 🧩 Aggregates results per severity
    public async Task<IEnumerable<ArchitectureSummaryItem>> GetSeveritySummaryAsync(int reportId, CancellationToken token = default)
    {
        return await _db.RuleResults
            .Where(r => r.ReportId == reportId)
            .GroupBy(r => r.Severity)
            .Select(g => new ArchitectureSummaryItem
            {
                Key = g.Key.ToString(),  // Enum → string
                Count = g.Count()
            })
            .ToListAsync(token);
    }
}
