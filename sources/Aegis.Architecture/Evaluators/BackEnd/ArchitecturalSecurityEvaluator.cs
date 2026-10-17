using System.Text.Json;
using System.Text.RegularExpressions;
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.BackEnd;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.BackEnd;

/// <summary>
/// Performs heuristic security checks against application source files,
/// dependency manifests, and compilation configuration.
///
/// Findings represent detected evidence, not proof of exploitability.
/// Compliance decisions belong to the registered rule engine.
/// </summary>
public sealed class ArchitecturalSecurityEvaluator
    : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly SecurityPolicy _policy;

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

    private static readonly string[] NodeDependencySections =
    [
        "dependencies",
        "devDependencies",
        "optionalDependencies",
        "peerDependencies"
    ];

    private static readonly Regex[] SecretPatterns =
    [
        new(
            @"\bAKIA[0-9A-Z]{16}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        new(
            @"(?i)\baws_secret_access_key\s*[:=]\s*['""]?[A-Za-z0-9/+=]{40}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        new(
            @"(?i)\b(subscriptionKey|primaryKey|clientSecret)\s*[:=]\s*['""][^'""]+['""]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        new(
            @"(?i)\bconnection\s*string\s*[:=]\s*['""][^'""]+['""]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        new(
            @"-----BEGIN\s+(?:RSA |EC |OPENSSH )?PRIVATE KEY-----",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        new(
            @"(?i)\b(jwt|token|bearer)\s*[:=]\s*['""][A-Za-z0-9_\-.]{20,}['""]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant),

        new(
            @"(?i)\bpassword\s*[:=]\s*['""][^'""]{6,}['""]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)
    ];

    private static readonly Regex WeakCryptoRegex = new(
        @"(?i)\b(MD5|SHA-?1|DES|RC4)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WeakTlsRegex = new(
        @"(?i)\bTLS\s*1(?:\.0|\.1)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PythonExactPinRegex = new(
        @"^\s*[A-Za-z0-9][A-Za-z0-9._-]*\s*==\s*[A-Za-z0-9][A-Za-z0-9.!+_-]*(?:\s*;.*)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public override string Name => "SecurityEvaluator";

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

    public ArchitecturalSecurityEvaluator(
        ILogger<ArchitecturalSecurityEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policy = options.Value.Security ?? new SecurityPolicy();
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (Context is null)
        {
            _logger.LogWarning(
                "{Evaluator}: no architecture context is available.",
                Name);

            return [];
        }

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Trace,
            $"Evaluating security for {Context.Language}/{Context.Framework}.");

        var results = new List<ArchitectureEvaluatorResult>();

        if (_policy.ScanForHardcodedSecrets)
        {
            results.AddRange(
                await ScanSecretsAsync(projectPath, token));
        }

        results.AddRange(
            await ScanDependenciesAsync(projectPath, token));

        results.AddRange(
            await ScanUnsafeCompilationAsync(projectPath, token));

        if (_policy.EnforceSafeCryptography ||
            _policy.DetectWeakSSLProtocols)
        {
            results.AddRange(
                await ScanCryptoAsync(projectPath, token));
        }

        var summary = CreateSummary(projectPath, results);
        results.Add(summary);

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Security evaluation completed with {results.Count - 1} finding records.");

        return results;
    }

    private async Task<IEnumerable<ArchitectureEvaluatorResult>>
        ScanSecretsAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        foreach (var file in GetSecurityFiles(projectPath))
        {
            token.ThrowIfCancellationRequested();

            var content = await TryReadFileAsync(file, token);

            if (content is null)
            {
                continue;
            }

            token.ThrowIfCancellationRequested();

            var matches = SecretPatterns.Sum(
                pattern => pattern.Matches(content).Count);

            if (matches == 0)
            {
                continue;
            }

            results.Add(
                CreateFileResult(
                    file,
                    "SecuritySecrets",
                    new Dictionary<string, double>
                    {
                        ["SecretsDetected"] = matches
                    },
                    "Potential hardcoded secrets detected. Textual heuristic; review each match."));
        }

        return results;
    }

    private async Task<IEnumerable<ArchitectureEvaluatorResult>>
        ScanDependenciesAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        foreach (var file in EnumerateApplicationFiles(projectPath))
        {
            token.ThrowIfCancellationRequested();

            var name = Path.GetFileName(file);

            if (name.Equals(
                    "package.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                var content = await TryReadFileAsync(file, token);

                if (content is null)
                {
                    continue;
                }

                var count = CountUnpinnedNodeDependencies(content);

                if (count is null)
                {
                    results.Add(
                        CreateFileResult(
                            file,
                            "SecurityDependencies",
                            new Dictionary<string, double>
                            {
                                ["DependencyManifestParseError"] = 1
                            },
                            "The package.json manifest could not be parsed. Dependency pinning was not evaluated."));

                    continue;
                }

                if (count.Value > 0)
                {
                    results.Add(
                        CreateFileResult(
                            file,
                            "SecurityDependencies",
                            new Dictionary<string, double>
                            {
                                ["UnpinnedDependencies"] = count.Value
                            },
                            "Dependency declarations contain wildcard, range, or mutable version specifications."));
                }
            }
            else if (name.Equals(
                         "requirements.txt",
                         StringComparison.OrdinalIgnoreCase))
            {
                var content = await TryReadFileAsync(file, token);

                if (content is null)
                {
                    continue;
                }

                var count = CountUnpinnedPythonDependencies(content);

                if (count > 0)
                {
                    results.Add(
                        CreateFileResult(
                            file,
                            "SecurityDependencies",
                            new Dictionary<string, double>
                            {
                                ["UnpinnedDependencies"] = count
                            },
                            "Python requirements without an exact == version pin were detected."));
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
        var results = new List<ArchitectureEvaluatorResult>();

        foreach (var file in EnumerateApplicationFiles(projectPath)
                     .Where(
                         path => path.EndsWith(
                             ".csproj",
                             StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();

            var content = await TryReadFileAsync(file, token);

            if (content is null)
            {
                continue;
            }

            // This is deliberately a textual indicator, not an XML policy evaluation.
            var enabled = Regex.IsMatch(
                content,
                @"<AllowUnsafeBlocks\s*>\s*true\s*</AllowUnsafeBlocks\s*>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!enabled)
            {
                continue;
            }

            results.Add(
                CreateFileResult(
                    file,
                    "SecurityConfiguration",
                    new Dictionary<string, double>
                    {
                        ["UnsafeCompilationEnabled"] = 1
                    },
                    "AllowUnsafeBlocks is enabled in the project file."));
        }

        return results;
    }

    private async Task<IEnumerable<ArchitectureEvaluatorResult>>
        ScanCryptoAsync(
            string projectPath,
            CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();

        foreach (var file in GetSecurityFiles(projectPath))
        {
            token.ThrowIfCancellationRequested();

            var content = await TryReadFileAsync(file, token);

            if (content is null)
            {
                continue;
            }

            var weakCrypto = _policy.EnforceSafeCryptography
                ? WeakCryptoRegex.Matches(content).Count
                : 0;

            var weakTls = _policy.DetectWeakSSLProtocols
                ? WeakTlsRegex.Matches(content).Count
                : 0;

            if (weakCrypto == 0 && weakTls == 0)
            {
                continue;
            }

            results.Add(
                CreateFileResult(
                    file,
                    "SecurityCrypto",
                    new Dictionary<string, double>
                    {
                        ["WeakCryptoMatches"] = weakCrypto,
                        ["WeakTLSMatches"] = weakTls
                    },
                    "Potential weak cryptography or TLS version reference detected. Verify usage and context."));
        }

        return results;
    }

    private static int? CountUnpinnedNodeDependencies(string content)
    {
        try
        {
            using var json = JsonDocument.Parse(content);

            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var count = 0;

            foreach (var sectionName in NodeDependencySections)
            {
                if (!json.RootElement.TryGetProperty(
                        sectionName,
                        out var section))
                {
                    continue;
                }

                if (section.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var dependency in section.EnumerateObject())
                {
                    if (dependency.Value.ValueKind != JsonValueKind.String)
                    {
                        count++;
                        continue;
                    }

                    var version = dependency.Value.GetString()?.Trim()
                                  ?? string.Empty;

                    if (IsUnpinnedNodeVersion(version))
                    {
                        count++;
                    }
                }
            }

            return count;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsUnpinnedNodeVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return true;
        }

        if (version.Equals(
                "latest",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // These are ranges, tags, workspace references, or mutable sources,
        // not exact version pins. A lockfile may still make installation reproducible.
        if (version.StartsWith(
                ("^"),
                StringComparison.Ordinal)
            || version.StartsWith("~", StringComparison.Ordinal)
            || version.StartsWith(">", StringComparison.Ordinal)
            || version.StartsWith("<", StringComparison.Ordinal)
            || version.Contains('*')
            || version.Contains('x', StringComparison.OrdinalIgnoreCase)
            || version.StartsWith("workspace:", StringComparison.OrdinalIgnoreCase)
            || version.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || version.StartsWith("git+", StringComparison.OrdinalIgnoreCase)
            || version.StartsWith("github:", StringComparison.OrdinalIgnoreCase)
            || version.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
            || version.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static int CountUnpinnedPythonDependencies(string content)
    {
        var count = 0;

        foreach (var rawLine in content.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // Ignore pip options and editable-install directives: this check
            // evaluates ordinary package requirements only.
            if (line.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            // Inline comments are removed before evaluating the requirement.
            var commentIndex = line.IndexOf(" #", StringComparison.Ordinal);

            if (commentIndex >= 0)
            {
                line = line[..commentIndex].TrimEnd();
            }

            if (line.Length == 0)
            {
                continue;
            }

            // Exact == pins are counted as pinned. Other operators such as >=,
            // ~=, >, and < allow a range and are counted as unpinned.
            if (!PythonExactPinRegex.IsMatch(line))
            {
                count++;
            }
        }

        return count;
    }

    private IEnumerable<string> GetSecurityFiles(string projectPath)
    {
        return EnumerateApplicationFiles(projectPath)
            .Where(
                file => SecurityExtensions.Any(
                    extension => file.EndsWith(
                        extension,
                        StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<string?> TryReadFileAsync(
        string file,
        CancellationToken token)
    {
        try
        {
            return await File.ReadAllTextAsync(file, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "{Evaluator}: could not read file {FilePath}.",
                Name,
                file);

            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(
                exception,
                "{Evaluator}: access denied for file {FilePath}.",
                Name,
                file);

            return null;
        }
    }

    private ArchitectureEvaluatorResult CreateFileResult(
        string file,
        string category,
        Dictionary<string, double> metrics,
        string evidence)
    {
        var layer = ResolveLayer(file);

        return new ArchitectureEvaluatorResult(Name, Path.GetFileName(file))
        {
            ProjectName = Context?.ProjectName,
            Language = Context?.Language,
            Framework = Context?.Framework,
            File = file,
            Layer = layer,
            DetectionConfidence = Context?.Confidence ?? 0,
            Category = category,
            Metrics = metrics,
            Metadata =
            {
                [EvaluationScopeMetadataKey] = FileScope,
                [FilePathMetadataKey] = file,
                ["Language"] = Context?.Language ?? "Unknown",
                ["Framework"] = Context?.Framework ?? "Unknown",
                ["Layer"] = layer ?? "Unknown",
                ["Evidence"] = evidence,
                ["DetectionMethod"] = "SourcePatternHeuristic"
            }
        };
    }

    private ArchitectureEvaluatorResult CreateSummary(
        string projectPath,
        IReadOnlyCollection<ArchitectureEvaluatorResult> results)
    {
        var findingCount = results.Sum(CountFindingEvidence);

        var secretCount = SumMetric(results, "SecretsDetected");
        var unpinnedDependencyCount = SumMetric(results, "UnpinnedDependencies");
        var weakCryptoCount = SumMetric(results, "WeakCryptoMatches");
        var weakTlsCount = SumMetric(results, "WeakTLSMatches");
        var unsafeCompilationCount = SumMetric(results, "UnsafeCompilationEnabled");
        var manifestErrorCount = SumMetric(results, "DependencyManifestParseError");

        return new ArchitectureEvaluatorResult(Name, "SecuritySummary")
        {
            ProjectName = Context?.ProjectName,
            Language = Context?.Language,
            Framework = Context?.Framework,
            DetectionConfidence = Context?.Confidence ?? 0,
            Category = "SecuritySummary",
            Metrics =
            {
                ["Findings"] = findingCount,
                ["FindingRecords"] = results.Count,
                ["SecretsDetected"] = secretCount,
                ["UnpinnedDependencies"] = unpinnedDependencyCount,
                ["WeakCryptoMatches"] = weakCryptoCount,
                ["WeakTLSMatches"] = weakTlsCount,
                ["UnsafeCompilationEnabled"] = unsafeCompilationCount,
                ["DependencyManifestParseErrors"] = manifestErrorCount
            },
            Metadata =
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["MetricScope"] = ProjectScope,
                ["ProjectPath"] = projectPath,
                ["Language"] = Context?.Language ?? "Unknown",
                ["Framework"] = Context?.Framework ?? "Unknown",
                ["DetectionMethod"] = "SourcePatternHeuristic",
                ["AnalysisStatus"] = manifestErrorCount > 0
                    ? "CompletedWithErrors"
                    : "Completed"
            }
        };
    }

    private static double CountFindingEvidence(
        ArchitectureEvaluatorResult result)
    {
        var metrics = result.Metrics;
        var count = 0.0;

        foreach (var key in new[]
                 {
                     "SecretsDetected",
                     "UnpinnedDependencies",
                     "UnsafeCompilationEnabled",
                     "WeakCryptoMatches",
                     "WeakTLSMatches",
                     "DependencyManifestParseError"
                 })
        {
            if (metrics.TryGetValue(key, out var value))
            {
                count += value;
            }
        }

        return count;
    }

    private static double SumMetric(
        IEnumerable<ArchitectureEvaluatorResult> results,
        string metricName)
    {
        return results.Sum(
            result => result.Metrics.TryGetValue(metricName, out var value)
                ? value
                : 0);
    }

    private string? ResolveLayer(string file)
    {
        return Context?
            .Layers
            .FirstOrDefault(
                layer => layer.Files.Contains(
                    file,
                    StringComparer.OrdinalIgnoreCase))
            ?.Name;
    }
}