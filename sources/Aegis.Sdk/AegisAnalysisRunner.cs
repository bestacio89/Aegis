using Microsoft.Extensions.Logging;

namespace Aegis.Sdk;
public sealed class AegisSecurityAnalysisRunner
{
    private readonly ISecurityRuleRegistry _registry;
    private readonly ILogger<AegisSecurityAnalysisRunner> _logger;

    public AegisSecurityAnalysisRunner(ISecurityRuleRegistry registry, ILogger<AegisSecurityAnalysisRunner> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<AegisSecurityReport> ExecuteAsync(string path, CancellationToken token = default)
    {
        _logger.LogInformation("🔒 Running Aegis Security Analysis...");
        var results = await _registry.ExecuteAllAsync(path, token);
        return new AegisSecurityReport { Findings = results.ToList() };
    }
}
