using Aegis;
using Aegis.App;
using Aegis.App.Wpf;
using Aegis.App.Wpf.Services;
using Aegis.App.Wpf.Services.Abstractions;
using Aegis.App.Wpf.Services.NewFolder;
using Aegis.App.Wpf.ViewModels;
using Aegis.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.App.Wpf.Services.NewFolder
{
    public sealed class LayerAnalysisService : ILayerAnalysisService
    {
        private readonly IRuleResultRepository _ruleRepo;
        private readonly IReportRepository _reportRepo;

        public LayerAnalysisService(
            IRuleResultRepository ruleRepo,
            IReportRepository reportRepo)
        {
            _ruleRepo = ruleRepo;
            _reportRepo = reportRepo;
        }

        public async Task<IReadOnlyList<LayerStat>> GetLayerStatsAsync(CancellationToken ct)
        {
            var reports = await _reportRepo.GetAllReportsAsync();
            var last = reports.OrderByDescending(r => r.ScanDate).FirstOrDefault();

            if (last is null)
                return [];

            var violations = await _ruleRepo.GetViolationsByReportIdAsync(last.Id, ct);

            if (violations is null)
                return [];

            return violations
                .GroupBy(v =>
                {
                    var t = v.Target ?? "";

                    if (t.Contains("Api", StringComparison.OrdinalIgnoreCase)) return "API";
                    if (t.Contains("App", StringComparison.OrdinalIgnoreCase)) return "Application";
                    if (t.Contains("Domain", StringComparison.OrdinalIgnoreCase)) return "Domain";
                    if (t.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase)) return "Infrastructure";

                    return "Other";
                })
                .Select(g => new LayerStat(g.Key, g.Count()))
                .OrderByDescending(x => x.Count)
                .ToList();
        }
    }
}
