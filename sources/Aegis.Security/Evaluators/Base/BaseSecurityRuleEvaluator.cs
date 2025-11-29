using Aegis.Shared.Security.Contracts;
using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Aegis.Shared.Security.Models.Rules;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Aegis.Security.Evaluators.Base;

public abstract class BaseSecurityEvaluator : ISecurityEvaluator
{
    protected readonly ILogger _logger;
    protected readonly IReadOnlyCollection<SecurityRuleDefinition> _rules;

    /// <summary>
    /// Domain handled by this evaluator (Network, Application, IaC, Secrets…)
    /// </summary>
    public abstract string Domain { get; }

    protected BaseSecurityEvaluator(
        ILogger logger,
        IReadOnlyCollection<SecurityRuleDefinition> rules)
    {
        _logger = logger;
        _rules = rules;
    }

    /// <summary>
    /// Evaluates all probe signals for this domain using rule definitions.
    /// </summary>
    public Task<SecurityEvaluationResult> EvaluateAsync(
        ProjectSecurityContext context,
        IEnumerable<SecuritySignal> signals,
        CancellationToken ct = default)
    {
        var results = new List<SecurityRuleResult>();

        // Filter only the signals relevant to this domain
        var domainSignals = signals
            .Where(s => string.Equals(s.Category, Domain, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var rule in _rules)
        {
            foreach (var signal in domainSignals)
            {
                if (!MatchesRule(rule, signal))
                    continue;

                var (target, location) = ExtractTargetAndLocation(signal);

                results.Add(new SecurityRuleResult
                {
                    RuleId = rule.RuleId,
                    Title = rule.Title,
                    Description = rule.Description,
                    Category = ResolveCategory(),
                    Vulnerability = rule.Vulnerability ?? VulnerabilityType.Unknown,
                    Severity = rule.Severity,
                    Risk = rule.RiskLevel,
                    Score = ComputeScore(rule),

                    Target = target,
                    Location = location,

                    Evidence = signal.Evidence,
                    Reference = rule.ReferenceUrl,
                    TimestampUtc = DateTimeOffset.UtcNow
                });
            }
        }

        return Task.FromResult(new SecurityEvaluationResult
        {
            Category = ResolveCategory(),
            TargetContext = context.ProjectPath,
            RuleResults = results
        });
    }

    // ---------------------------- Helpers ---------------------------- //

    private static bool MatchesRule(SecurityRuleDefinition rule, SecuritySignal signal)
    {
        // Regex evidence pattern
        if (!string.IsNullOrWhiteSpace(rule.EvidencePattern))
        {
            var regex = new Regex(rule.EvidencePattern, RegexOptions.IgnoreCase);
            if (regex.IsMatch(signal.Evidence ?? string.Empty))
                return true;
        }

        // Artifact match
        if (rule.AffectedArtifacts?.Length > 0)
        {
            if (rule.AffectedArtifacts.Any(a =>
                    (signal.Evidence ?? "").Contains(a, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (signal.Metadata != null)
            {
                foreach (var kv in signal.Metadata)
                {
                    if (kv.Value is string s &&
                        rule.AffectedArtifacts.Any(a =>
                            s.Contains(a, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }
        }

        // Tag match
        if (rule.Tags != null && signal.Metadata != null)
        {
            if (rule.Tags.Any(tag => signal.Metadata.ContainsKey(tag)))
                return true;
        }

        return false;
    }

    private static (string Target, string? Location) ExtractTargetAndLocation(SecuritySignal signal)
    {
        if (signal.Metadata != null &&
            signal.Metadata.TryGetValue("target", out var t) &&
            t is string target &&
            !string.IsNullOrWhiteSpace(target))
        {
            return (target, null);
        }

        if (!string.IsNullOrWhiteSpace(signal.Evidence))
        {
            var lines = signal.Evidence.Split('\n');
            string first = lines.FirstOrDefault()?.Trim() ?? string.Empty;

            string? location = lines.Length > 1
                ? lines.Skip(1).FirstOrDefault(l => l.Contains("line", StringComparison.OrdinalIgnoreCase))
                : null;

            return (string.IsNullOrWhiteSpace(first) ? signal.DetectorId : first, location);
        }

        return (signal.DetectorId, null);
    }

    private static double ComputeScore(SecurityRuleDefinition rule)
    {
        if (rule.MinScore > 0 || rule.MaxScore > 0)
            return Math.Round((rule.MinScore + rule.MaxScore) / 2.0, 2);

        return SecuritySeverityThresholds.GetDefaultThreshold(rule.Severity);
    }

    private SecurityCategory ResolveCategory()
    {
        if (Enum.TryParse<SecurityCategory>(Domain, true, out var cat))
            return cat;

        return SecurityCategory.Unknown;
    }
}
