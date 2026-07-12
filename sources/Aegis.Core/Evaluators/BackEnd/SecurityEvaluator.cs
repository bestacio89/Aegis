using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.BackEnd;

/// <summary>
/// Evaluates application security practices from detected application boundaries.
///
/// The evaluator consumes the centralized architecture context and produces
/// security facts only. Rule interpretation, severity and remediation decisions
/// are handled by the RuleEngine.
///
/// Responsibilities:
/// - Hardcoded secret detection
/// - Dependency pinning analysis
/// - Unsafe compilation settings
/// - Weak cryptography/TLS detection
/// </summary>
public sealed class SecurityEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly SecurityPolicy _policy;



    public override string Name =>
        "SecurityEvaluator";



    public override string[] SupportedLanguages =>
    [
        "C#",
        "Python",
        "JavaScript",
        "TypeScript",
        "Java"
    ];



    public override string[] SupportedFrameworks =>
    [
        "ASP.NET",
        "FastAPI",
        "Flask",
        "Spring",
        "Node.js"
    ];



    private static readonly string[] SecurityExtensions =
    [
        ".cs",
        ".json",
        ".yml",
        ".yaml",
        ".ts",
        ".js",
        ".py",
        ".env",
        ".config"
    ];



    private static readonly Regex[] SecretPatterns =
    [
        new(
            @"AKIA[0-9A-Z]{16}",
            RegexOptions.Compiled),

        new(
            @"(?i)aws_secret_access_key\s*[:=]\s*['""]?[A-Za-z0-9/+=]{40}",
            RegexOptions.Compiled),

        new(
            @"(?i)(subscriptionKey|primaryKey|clientSecret|tenantId)\s*[:=]\s*['""][^'""]+['""]",
            RegexOptions.Compiled),

        new(
            @"(?i)connection\s*string\s*[:=]\s*['""][^'""]+['""]",
            RegexOptions.Compiled),

        new(
            @"-----BEGIN\s+PRIVATE\s+KEY-----",
            RegexOptions.Compiled),

        new(
            @"(?i)(jwt|token|bearer)\s*[:=]\s*['""][A-Za-z0-9-_\.]{20,}['""]",
            RegexOptions.Compiled),

        new(
            @"(?i)password\s*[:=]\s*['""][^'""]{6,}['""]",
            RegexOptions.Compiled)
    ];



    public SecurityEvaluator(
        ILogger<SecurityEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        _policy =
            options.Value.Security
            ?? new SecurityPolicy();
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        if (Context is null)
            return results;



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Evaluating security for {Context.Language}/{Context.Framework}.");



        if (_policy.ScanForHardcodedSecrets)
        {
            results.AddRange(
                await ScanSecretsAsync(
                    projectPath,
                    token));
        }



        results.AddRange(
            await ScanDependenciesAsync(
                projectPath,
                token));



        results.AddRange(
            await ScanUnsafeCompilationAsync(
                projectPath,
                token));



        if (_policy.EnforceSafeCryptography ||
            _policy.DetectWeakSSLProtocols)
        {
            results.AddRange(
                await ScanCryptoAsync(
                    projectPath,
                    token));
        }



        results.Add(
            CreateSummary(
                projectPath,
                results));



        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Security evaluation completed with {results.Count} entries.");



        return results;
    }



    private async Task<IEnumerable<ArchitectureEvaluatorResult>>
        ScanSecretsAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        foreach (var file in GetSecurityFiles(projectPath))
        {
            token.ThrowIfCancellationRequested();



            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



            var matches =
                SecretPatterns.Sum(
                    pattern =>
                        pattern.Matches(content).Count);



            if (matches == 0)
                continue;



            results.Add(
                CreateResult(
                    file,
                    "SecuritySecrets",
                    new()
                    {
                        ["SecretsDetected"] = matches
                    }));
        }



        return results;
    }



    private async Task<IEnumerable<ArchitectureEvaluatorResult>>
        ScanDependenciesAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        foreach (var file in EnumerateApplicationFiles(projectPath))
        {
            token.ThrowIfCancellationRequested();



            var fileName =
                Path.GetFileName(file);



            if (fileName.Equals(
                    "package.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                var count =
                    await CountUnpinnedNodeDependencies(
                        file,
                        token);



                if (count > 0)
                {
                    results.Add(
                        CreateResult(
                            file,
                            "SecurityDependencies",
                            new()
                            {
                                ["UnpinnedDependencies"] = count
                            }));
                }
            }



            if (fileName.Equals(
                    "requirements.txt",
                    StringComparison.OrdinalIgnoreCase))
            {
                var count =
                    await CountUnpinnedPythonDependencies(
                        file,
                        token);



                if (count > 0)
                {
                    results.Add(
                        CreateResult(
                            file,
                            "SecurityDependencies",
                            new()
                            {
                                ["UnpinnedDependencies"] = count
                            }));
                }
            }
        }



        return results;
    }



    private async Task<IEnumerable<ArchitectureEvaluatorResult>>
        ScanUnsafeCompilationAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        foreach (var file in EnumerateApplicationFiles(projectPath)
            .Where(
                x =>
                    x.EndsWith(
                        ".csproj",
                        StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();



            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



            if (!content.Contains(
                    "<AllowUnsafeBlocks>true",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }



            results.Add(
                CreateResult(
                    file,
                    "SecurityConfiguration",
                    new()
                    {
                        ["UnsafeCompilationEnabled"] = 1
                    }));
        }



        return results;
    }



    private async Task<IEnumerable<ArchitectureEvaluatorResult>>
        ScanCryptoAsync(
            string projectPath,
            CancellationToken token)
    {
        var results =
            new List<ArchitectureEvaluatorResult>();



        foreach (var file in GetSecurityFiles(projectPath))
        {
            token.ThrowIfCancellationRequested();



            var content =
                await File.ReadAllTextAsync(
                    file,
                    token);



            var weakCrypto =
                _policy.EnforceSafeCryptography
                    ? Regex.Matches(
                        content,
                        @"(?i)(MD5|SHA1|DES|RC4)")
                    .Count
                    : 0;



            var weakTls =
                _policy.DetectWeakSSLProtocols
                    ? Regex.Matches(
                        content,
                        @"(?i)TLS\s*1(\.0|\.1)")
                    .Count
                    : 0;



            if (weakCrypto == 0 &&
                weakTls == 0)
            {
                continue;
            }



            results.Add(
                CreateResult(
                    file,
                    "SecurityCrypto",
                    new()
                    {
                        ["WeakCryptoMatches"] = weakCrypto,
                        ["WeakTLSMatches"] = weakTls
                    }));
        }



        return results;
    }



    private async Task<int> CountUnpinnedNodeDependencies(
        string file,
        CancellationToken token)
    {
        try
        {
            using var json =
                JsonDocument.Parse(
                    await File.ReadAllTextAsync(
                        file,
                        token));



            if (!json.RootElement.TryGetProperty(
                    "dependencies",
                    out var dependencies))
            {
                return 0;
            }



            return dependencies
                .EnumerateObject()
                .Count(
                    dependency =>
                    {
                        var version =
                            dependency.Value.GetString()
                            ?? string.Empty;


                        return
                            version.Contains("*")
                            ||
                            version.StartsWith("^0.")
                            ||
                            version.Equals(
                                "latest",
                                StringComparison.OrdinalIgnoreCase);
                    });
        }
        catch
        {
            return 0;
        }
    }



    private static async Task<int>
        CountUnpinnedPythonDependencies(
            string file,
            CancellationToken token)
    {
        var lines =
            await File.ReadAllLinesAsync(
                file,
                token);



        return lines.Count(
            line =>
                !string.IsNullOrWhiteSpace(line)
                &&
                !line.StartsWith("#")
                &&
                !Regex.IsMatch(
                    line,
                    @"(==|>=|~=)"));
    }



    private IEnumerable<string> GetSecurityFiles(
        string projectPath)
    {
        return EnumerateApplicationFiles(projectPath)
            .Where(
                file =>
                    SecurityExtensions.Any(
                        ext =>
                            file.EndsWith(
                                ext,
                                StringComparison.OrdinalIgnoreCase)));
    }



    private ArchitectureEvaluatorResult CreateResult(
        string file,
        string category,
        Dictionary<string, double> metrics)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            file)
        {
            ProjectName =
                Context?.ProjectName ?? string.Empty,

            Language =
                Context?.Language ?? "Unknown",

            Framework =
                Context?.Framework,

            Category =
                category,

            Layer =
                ResolveLayer(file),

            DetectionConfidence =
                Context?.Confidence ?? 0,

            Metrics =
                metrics,

            Metadata =
            {
                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown"
            }
        };
    }



    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        IEnumerable<ArchitectureEvaluatorResult> results)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            projectPath)
        {
            ProjectName =
                Context?.ProjectName ?? string.Empty,

            Category =
                "SecuritySummary",

            Metrics =
            {
                ["Findings"] =
                    results.Count()
            },

            Metadata =
            {
                ["Language"] =
                    Context?.Language ?? "Unknown",

                ["Framework"] =
                    Context?.Framework ?? "Unknown"
            }
        };
    }



    private string? ResolveLayer(
        string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(
                layer =>
                    layer.Files.Contains(
                        file,
                        StringComparer.OrdinalIgnoreCase))
            ?.Name;
    }
}