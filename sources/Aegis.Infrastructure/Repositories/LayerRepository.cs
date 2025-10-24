using Aegis.Infrastructure.Data;
using Aegis.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Infrastructure.Repositories;

public sealed class DashboardRepository
{
    private readonly AegisDbContext _db;

    public DashboardRepository(AegisDbContext db) => _db = db;

    // 🧩 Aggregates results per layer
    public async Task<IEnumerable<SummaryItem>> GetLayerSummaryAsync(int reportId, CancellationToken token = default)
    {
        return await _db.RuleResults
            .Where(r => r.ReportId == reportId)
            .GroupBy(r => r.Domain)
            .Select(g => new SummaryItem
            {
                Key = g.Key ?? "Unknown",
                Count = g.Count()
            })
            .ToListAsync(token);
    }

    // 🧩 Aggregates results per rule category
    public async Task<IEnumerable<SummaryItem>> GetRuleCategorySummaryAsync(int reportId, CancellationToken token = default)
    {
        return await _db.RuleResults
            .Where(r => r.ReportId == reportId)
            .GroupBy(r => r.Category)
            .Select(g => new SummaryItem
            {
                Key = g.Key ?? "Unknown",
                Count = g.Count()
            })
            .ToListAsync(token);
    }

    // 🧩 Aggregates results per severity
    public async Task<IEnumerable<SummaryItem>> GetSeveritySummaryAsync(int reportId, CancellationToken token = default)
    {
        return await _db.RuleResults
            .Where(r => r.ReportId == reportId)
            .GroupBy(r => r.Severity)
            .Select(g => new SummaryItem
            {
                Key = g.Key.ToString(),  // Enum → string
                Count = g.Count()
            })
            .ToListAsync(token);
    }
}
