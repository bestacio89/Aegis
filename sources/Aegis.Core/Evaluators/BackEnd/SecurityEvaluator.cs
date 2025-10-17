using Aegis.Core.Diagnostics;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Aegis.Shared.Models.Policies;
using Aegis.Shared.Models.Policies.BackEnd;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aegis.Core.Evaluators.BackEnd;

/// <summary>
/// Scans the codebase for potential security vulnerabilities and configuration risks.
/// Collects metrics across hardcoded secrets, dependency pinning, unsafe project options,
/// and weak cryptography/TLS configurations.
/// </summary>
public sealed class SecurityEvaluator : BaseEvaluator, IScopedDependency
{
    private readonly SecurityPolicy _policy;

    public override string Name => "SecurityEvaluator";
    public override string[] SupportedLanguages => ["CSharp", "Python", "JavaScript", "TypeScript", "Java"];
    public override string[] SupportedFrameworks => ["ASP.NET", "FastAPI", "Flask", "Spring", "Node.js"];

    private static readonly Regex[] SecretPatterns =
    [
        new(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled),                            // AWS Access Key
        new(@"(?i)aws_secret_access_key\s*[:=]\s*['""]?[A-Za-z0-9/+=]{40}", RegexOptions.Compiled),
        new(@"(?i)(subscriptionKey|primaryKey|clientSecret|tenantId)\s*[:=]\s*['""][^'""]+['""]", RegexOptions.Compiled),
        new(@"(?i)connection\s*string\s*[:=]\s*['""][^'""]+['""]", RegexOptions.Compiled),
        new(@"-----BEGIN\s+PRIVATE\s+KEY-----", RegexOptions.Compiled),
        new(@"(?i)(jwt|token|bearer)\s*[:=]\s*['""][A-Za-z0-9-_\.]{20,}\s*['""]", RegexOptions.Compiled),
        new(@"(?i)password\s*[:=]\s*['""][^'""]{6,}['""]", RegexOptions.Compiled),
    ];

    public SecurityEvaluator(
        ILogger<SecurityEvaluator> logger,
        IOptions<AegisPolicy> options)
        : base(logger)
    {
        _policy = options.Value.Security ?? new SecurityPolicy();
    }

    protected override async Task<IEnumerable<EvaluatorResult>> EvaluateCoreAsync(string projectPath, CancellationToken token)
    {
        var results = new List<EvaluatorResult>();

        AegisDiagnostics.Report(Name, DiagnosticLevel.Trace,
            $"🛡️ Security metric collection started for {Context?.Language ?? "Unknown"} / {Context?.Framework ?? "N/A"}");

        // 1️⃣ Secrets
        if (_policy.ScanForHardcodedSecrets)
            results.AddRange(await ScanForSecretsAsync(projectPath, token));

        // 2️⃣ Dependency pinning (npm, pip, csproj)
        results.AddRange(await ScanPackageJsonAsync(projectPath, token));
        results.AddRange(await ScanRequirementsAsync(projectPath, token));
        results.AddRange(await ScanCsprojUnsafeAsync(projectPath, token));

        // 3️⃣ Weak crypto / TLS
        if (_policy.EnforceSafeCryptography || _policy.DetectWeakSSLProtocols)
            results.AddRange(await ScanCryptoAndSSLAsync(projectPath, token));

        // 🧮 Project-level summary
        if (results.Count > 0)
        {
            var metricsByCategory = results
                .GroupBy(r => r.Category?? "Unknown")
                .ToDictionary(
                    g => g.Key,
                    g => (double)g.Count());

            results.Add(new EvaluatorResult(Name, projectPath)
            {
                Category = "SecuritySummary",
                Metrics = metricsByCategory,
                Metadata = new Dictionary<string, string>
                {
                    ["Language"] = Context?.Language ?? "Unknown",
                    ["Framework"] = Context?.Framework ?? "Unknown",
                    ["ScanForHardcodedSecrets"] = _policy.ScanForHardcodedSecrets.ToString(),
                    ["EnforceSafeCryptography"] = _policy.EnforceSafeCryptography.ToString(),
                    ["DetectWeakSSLProtocols"] = _policy.DetectWeakSSLProtocols.ToString()
                }
            });
        }

        AegisDiagnostics.Report(Name, DiagnosticLevel.Info,
            $"🛡️ Security evaluation completed — {results.Count} metric entries collected.");

        return results;
    }

    // =====================================================
    // 1️⃣ Secret Detection
    // =====================================================
    private async Task<IEnumerable<EvaluatorResult>> ScanForSecretsAsync(string projectPath, CancellationToken token)
    {
        var metrics = new List<EvaluatorResult>();

        var sourceFiles = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => new[] { ".cs", ".json", ".yml", ".yaml", ".ts", ".js", ".py", ".env" }
                .Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(Path.GetDirectoryName(f)!));

        foreach (var file in sourceFiles)
        {
            token.ThrowIfCancellationRequested();
            string text;
            try { text = await File.ReadAllTextAsync(file, token); }
            catch { continue; }

            int secretMatches = SecretPatterns.Sum(rx => rx.Matches(text).Count);
            if (secretMatches > 0)
            {
                metrics.Add(new EvaluatorResult(Name, file)
                {
                    Category = "SecuritySecrets",
                    Metrics = new Dictionary<string, double>
                    {
                        ["SecretsDetected"] = secretMatches
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["Language"] = Context?.Language ?? "Unknown",
                        ["PatternCount"] = SecretPatterns.Length.ToString(),
                        ["Policy_ScanEnabled"] = _policy.ScanForHardcodedSecrets.ToString()
                    }
                });
            }
        }

        return metrics;
    }

    // =====================================================
    // 2️⃣ JS/TS Dependency Audit
    // =====================================================
    private async Task<IEnumerable<EvaluatorResult>> ScanPackageJsonAsync(string projectPath, CancellationToken token)
    {
        var metrics = new List<EvaluatorResult>();

        foreach (var pkg in Directory.EnumerateFiles(projectPath, "package.json", SearchOption.AllDirectories)
                                     .Where(f => !IsExcludedDir(Path.GetDirectoryName(f)!)))
        {
            int unpinnedCount = 0;

            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(pkg, token));
                if (doc.RootElement.TryGetProperty("dependencies", out var deps))
                {
                    foreach (var dep in deps.EnumerateObject())
                    {
                        var ver = dep.Value.GetString() ?? "";
                        if (ver.Contains("*") || ver.StartsWith("^0.") || ver.StartsWith("latest", StringComparison.OrdinalIgnoreCase))
                            unpinnedCount++;
                    }
                }
            }
            catch { /* ignore */ }

            if (unpinnedCount > 0)
            {
                metrics.Add(new EvaluatorResult(Name, pkg)
                {
                    Category = "SecurityDependencies",
                    Metrics = new Dictionary<string, double>
                    {
                        ["UnpinnedDependencies"] = unpinnedCount
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["Language"] = "JavaScript/TypeScript",
                        ["Policy_RequirePinnedDeps"] = "True"
                    }
                });
            }
        }

        return metrics;
    }

    // =====================================================
    // 3️⃣ Python Dependency Audit
    // =====================================================
    private async Task<IEnumerable<EvaluatorResult>> ScanRequirementsAsync(string projectPath, CancellationToken token)
    {
        var metrics = new List<EvaluatorResult>();

        foreach (var req in Directory.EnumerateFiles(projectPath, "requirements.txt", SearchOption.AllDirectories)
                                     .Where(f => !IsExcludedDir(Path.GetDirectoryName(f)!)))
        {
            var lines = await File.ReadAllLinesAsync(req, token);
            int unpinned = lines
                .Count(line => !string.IsNullOrWhiteSpace(line)
                            && !line.StartsWith("#")
                            && !Regex.IsMatch(line, @"(==|>=|~=)"));

            if (unpinned > 0)
            {
                metrics.Add(new EvaluatorResult(Name, req)
                {
                    Category = "SecurityDependencies",
                    Metrics = new Dictionary<string, double>
                    {
                        ["UnpinnedDependencies"] = unpinned
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["Language"] = "Python",
                        ["Policy_RequirePinnedDeps"] = "True"
                    }
                });
            }
        }

        return metrics;
    }

    // =====================================================
    // 4️⃣ .NET Unsafe Compilation Audit
    // =====================================================
    private async Task<IEnumerable<EvaluatorResult>> ScanCsprojUnsafeAsync(string projectPath, CancellationToken token)
    {
        var metrics = new List<EvaluatorResult>();
        var csprojs = Directory.EnumerateFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
                               .Where(f => !IsExcludedDir(Path.GetDirectoryName(f)!));

        foreach (var csproj in csprojs)
        {
            var xml = await File.ReadAllTextAsync(csproj, token);
            if (xml.Contains("<AllowUnsafeBlocks>true", StringComparison.OrdinalIgnoreCase))
            {
                metrics.Add(new EvaluatorResult(Name, csproj)
                {
                    Category = "SecurityConfiguration",
                    Metrics = new Dictionary<string, double>
                    {
                        ["UnsafeCompilationEnabled"] = 1
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["Language"] = "CSharp",
                        ["Policy_AllowUnsafeBlocks"] = "False"
                    }
                });
            }
        }

        return metrics;
    }

    // =====================================================
    // 5️⃣ Cryptography & TLS Security Audit
    // =====================================================
    private async Task<IEnumerable<EvaluatorResult>> ScanCryptoAndSSLAsync(string projectPath, CancellationToken token)
    {
        var metrics = new List<EvaluatorResult>();
        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(f => new[] { ".cs", ".java", ".py", ".config", ".json" }
                .Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !IsExcludedDir(Path.GetDirectoryName(f)!));

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(file, token);

            int weakCrypto = 0, weakTls = 0;

            if (_policy.EnforceSafeCryptography)
                weakCrypto = Regex.Matches(content, @"(?i)(MD5|SHA1|DES|RC4)").Count;

            if (_policy.DetectWeakSSLProtocols)
                weakTls = Regex.Matches(content, @"(?i)TLS\s*1(\.0|\.1)").Count;

            if (weakCrypto > 0 || weakTls > 0)
            {
                metrics.Add(new EvaluatorResult(Name, file)
                {
                    Category = "SecurityCrypto",
                    Metrics = new Dictionary<string, double>
                    {
                        ["WeakCryptoMatches"] = weakCrypto,
                        ["WeakTLSMatches"] = weakTls
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["Language"] = Context?.Language ?? "Unknown",
                        ["Policy_EnforceSafeCryptography"] = _policy.EnforceSafeCryptography.ToString(),
                        ["Policy_DetectWeakSSLProtocols"] = _policy.DetectWeakSSLProtocols.ToString()
                    }
                });
            }
        }

        return metrics;
    }
}
