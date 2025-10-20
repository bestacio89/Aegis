using Aegis.Shared.Enums;
using Aegis.Shared.Models.Rules;

namespace Aegis.Infrastructure.Aggregation;

public sealed class LayerAggregator
{
    public Dictionary<string, CategorySummary> BuildCategorySummaries(IEnumerable<RuleResult> results)
    {
        var categories = new Dictionary<string, CategorySummary>(StringComparer.OrdinalIgnoreCase);

        var groupedByCategory = results.GroupBy(r => r.Category.ToString());
        foreach (var catGroup in groupedByCategory)
        {
            var catSummary = new CategorySummary
            {
                CategoryName = catGroup.Key,
                TotalViolations = catGroup.Count(),
                HealthIndex = ComputeHealth(catGroup),
                SeverityBreakdown = catGroup.GroupBy(r => r.Severity)
                    .ToDictionary(g => g.Key, g => g.Count()),
                Layers = BuildLayerSummaries(catGroup)
            };
            categories[catGroup.Key] = catSummary;
        }

        return categories;
    }

    public Dictionary<string, LayerSummary> BuildLayerSummaries(IEnumerable<RuleResult> results)
    {
        var layers = new Dictionary<string, LayerSummary>(StringComparer.OrdinalIgnoreCase);

        var grouped = results.GroupBy(r => r.Domain ?? "Unclassified");
        foreach (var group in grouped)
        {
            var summary = new LayerSummary
            {
                LayerName = group.Key,
                TotalFiles = group.Select(r => r.FilePath).Distinct().Count(),
                Violations = group.Count(),
                HealthIndex = ComputeHealth(group),
                SeverityBreakdown = group
                    .GroupBy(r => r.Severity)
                    .ToDictionary(g => g.Key, g => g.Count()),
                TopViolations = group.OrderByDescending(r => (int)r.Severity).Take(10).ToList()
            };
            AddBasicRecommendations(summary);
            layers[group.Key] = summary;
        }

        return layers;
    }

    private static double ComputeHealth(IEnumerable<RuleResult> rules)
    {
        if (!rules.Any()) return 100;
        var penalty = rules.Sum(r => (int)r.Severity * 5);
        return Math.Clamp(100 - penalty / Math.Max(1, rules.Count()), 0, 100);
    }

    private static void AddBasicRecommendations(LayerSummary summary)
    {
        switch (summary.LayerName.ToLowerInvariant())
        {
            case "api":
                summary.Recommendations.Add("Reduce direct infrastructure dependencies.");
                break;
            case "application":
                summary.Recommendations.Add("Enforce CQRS and clear command/query segregation.");
                break;
            case "domain":
                summary.Recommendations.Add("Simplify domain entities and isolate business rules.");
                break;
            case "infrastructure":
                summary.Recommendations.Add("Ensure repository and logging patterns remain isolated.");
                break;
            case "frontend":
                summary.Recommendations.Add("Enforce component modularity and reduce bundle size.");
                break;
        }
    }
}
