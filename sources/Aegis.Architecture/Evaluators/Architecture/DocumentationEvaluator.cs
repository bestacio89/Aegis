
using System.Text.RegularExpressions;
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Evaluators.Architecture;

/// <summary>
/// Estimates documentation coverage of discoverable public declarations.
/// Emits project-level measurements; RuleEngineCore owns compliance decisions.
/// This is a source-pattern heuristic, not compiler-backed analysis.
/// </summary>
public sealed class DocumentationEvaluator :
    BaseArchitectureEvaluator,
    IScopedDependency
{
    private static readonly RegexOptions SourceRegexOptions =
        RegexOptions.Compiled
        | RegexOptions.Multiline
        | RegexOptions.CultureInvariant;

    private static readonly Regex CSharpDeclarationRegex = new(
        @"^[ \t]*public\s+(?:(?:abstract|static|sealed|partial|virtual|override|async|new|readonly|unsafe|extern)\s+)*(?:(?:class|interface|record(?:\s+(?:class|struct))?|struct|enum|delegate)\s+\w+|(?:(?:[\w<>,?.\[\]\(\):]+\s+)+\w+\s*(?:\([^;{}]*\)\s*(?:where\s+[^{}]+)?|(?:\{|\=\>|\=\s*[^;]+;)))",
        SourceRegexOptions);

    private static readonly Regex JavaDeclarationRegex = new(
        @"^[ \t]*public\s+(?:(?:abstract|static|final|synchronized|native|default|strictfp)\s+)*(?:(?:class|interface|enum|record)\s+\w+|(?:[\w<>,?.\[\]@]+\s+)+\w+\s*\([^;{}]*\)\s*(?:throws\s+[\w., ]+)?\s*\{?|(?:[\w<>,?.\[\]@]+\s+)+\w+\s*(?:=[^;]*)?;)",
        SourceRegexOptions);

    private static readonly Regex TypeScriptDeclarationRegex = new(
        @"^[ \t]*(?:export\s+(?:default\s+)?|declare\s+export\s+)?(?:(?:abstract|declare|async)\s+)*(?:class|interface|type|function|enum|namespace)\s+\w+|^[ \t]*export\s+(?:declare\s+)?(?:const|let|var)\s+\w+",
        SourceRegexOptions);

    private static readonly Regex PythonDeclarationRegex = new(
        @"^[ \t]*(?:async\s+)?(?:def|class)\s+([A-Za-z]\w*)\s*(?:\([^:\r\n]*\))?\s*(?:->\s*[^:\r\n]+)?\s*:",
        SourceRegexOptions);

    private static readonly Regex CSharpXmlDocumentationRegex = new(
        @"^[ \t]*///",
        SourceRegexOptions);

    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "test",
            "tests",
            "testing",
            "generated",
            "obj",
            "bin",
            "node_modules",
            ".git"
        };

    public override string Name => "DocumentationEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "TypeScript",
        "JavaScript",
        "Java",
        "Python"
    ];

    public override string[] SupportedFrameworks => ["*"];

    public DocumentationEvaluator(
        ILogger<DocumentationEvaluator> logger)
        : base(logger)
    {
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (Context is null)
        {
            return [];
        }

        var extensions = GetExtensions(Context.Language);

        if (extensions.Length == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Warning,
                $"Documentation analysis skipped: unsupported language '{Context.Language}'.");

            return [];
        }

        var files = ResolveSourceFiles(projectPath, extensions)
            .Where(IsEligibleSourceFile)
            .ToList();

        var totalDeclarations = 0;
        var documentedDeclarations = 0;
        var analyzedFiles = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not read source file {SourceFile} during documentation analysis.",
                    file);

                continue;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Access denied while reading source file {SourceFile} during documentation analysis.",
                    file);

                continue;
            }

            token.ThrowIfCancellationRequested();

            var counts = CountDeclarations(content, Context.Language);

            totalDeclarations += counts.Total;
            documentedDeclarations += counts.Documented;
            analyzedFiles++;
        }

        if (totalDeclarations == 0)
        {
            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Warning,
                $"Documentation analysis found no eligible public declarations. " +
                $"Files considered: {files.Count}; files read: {analyzedFiles}.");

            return [];
        }

        var coverage = documentedDeclarations * 100.0 / totalDeclarations;

        var result = CreateProjectSummary(
            coverage,
            totalDeclarations,
            documentedDeclarations,
            analyzedFiles,
            files.Count);

        AegisDiagnostics.Report(
            Name,
            DiagnosticLevel.Info,
            $"Documentation analysis completed. " +
            $"Coverage={coverage:0.0}%; " +
            $"Documented={documentedDeclarations}; " +
            $"Declarations={totalDeclarations}; " +
            $"FilesRead={analyzedFiles}.");

        return [result];
    }

    private ArchitectureEvaluatorResult CreateProjectSummary(
        double coverage,
        int totalDeclarations,
        int documentedDeclarations,
        int analyzedFiles,
        int discoveredFiles)
    {
        return new ArchitectureEvaluatorResult(
            Name,
            "DocumentationSummary")
        {
            Category = nameof(ArchitectureRuleCategory.Architecture),

            Metrics = new Dictionary<string, double>
            {
                ["DocumentationCoverage"] = coverage,
                ["DocumentationCompliance"] = coverage,
                ["TotalDeclarations"] = totalDeclarations,
                ["DocumentedDeclarations"] = documentedDeclarations,
                ["MissingDocumentation"] =
                    totalDeclarations - documentedDeclarations,
                ["DocumentationAnalysisFileCount"] = analyzedFiles,
                ["DocumentationDiscoveredFileCount"] = discoveredFiles
            },

            Metadata = new Dictionary<string, string>
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["MetricScope"] = ProjectScope,
                ["Language"] = Context?.Language ?? "Unknown",
                ["Framework"] = Context?.Framework ?? "Unknown",
                ["MeasurementMethod"] = "LanguageSpecificSourceHeuristics",
                ["CoverageUnit"] = "Percent",
                ["AnalysisStatus"] = "Completed"
            }
        };
    }

    private static DeclarationCounts CountDeclarations(
        string content,
        string language)
    {
        return language switch
        {
            "C#" => CountCommentDocumentedDeclarations(
                content,
                CSharpDeclarationRegex,
                CSharpXmlDocumentationRegex),

            "Java" => CountCommentDocumentedDeclarations(
                content,
                JavaDeclarationRegex,
                null),

            "TypeScript" or "JavaScript" =>
                CountCommentDocumentedDeclarations(
                    content,
                    TypeScriptDeclarationRegex,
                    null),

            "Python" => CountPythonDeclarations(content),

            _ => new DeclarationCounts(0, 0)
        };
    }

    private static DeclarationCounts CountCommentDocumentedDeclarations(
        string content,
        Regex declarationRegex,
        Regex? documentationRegex)
    {
        var declarations = declarationRegex.Matches(content);
        var documentedCount = 0;

        foreach (Match declaration in declarations)
        {
            if (HasPrecedingDocumentation(
                    content,
                    declaration.Index,
                    documentationRegex))
            {
                documentedCount++;
            }
        }

        return new DeclarationCounts(
            declarations.Count,
            documentedCount);
    }

    private static bool HasPrecedingDocumentation(
        string content,
        int declarationIndex,
        Regex? documentationRegex)
    {
        var prefix = content[..declarationIndex].TrimEnd();

        if (prefix.Length == 0)
        {
            return false;
        }

        if (documentationRegex is not null)
        {
            var lastLineStart = prefix.LastIndexOf('\n');
            var lastLine = prefix[(lastLineStart + 1)..];

            return documentationRegex.IsMatch(lastLine);
        }

        var lastClose = prefix.LastIndexOf("*/", StringComparison.Ordinal);

        if (lastClose < 0)
        {
            return false;
        }

        var lastOpen = prefix.LastIndexOf(
            "/**",
            lastClose,
            StringComparison.Ordinal);

        if (lastOpen < 0)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(prefix[(lastClose + 2)..]);
    }

    private static DeclarationCounts CountPythonDeclarations(
        string content)
    {
        var declarations = PythonDeclarationRegex.Matches(content);
        var publicDeclarations = declarations
            .Cast<Match>()
            .Where(declaration =>
                !declaration.Groups[1].Value.StartsWith(
                    "_",
                    StringComparison.Ordinal))
            .ToList();

        var documentedCount = publicDeclarations.Count(declaration =>
            HasPythonDocstring(content, declaration));

        return new DeclarationCounts(
            publicDeclarations.Count,
            documentedCount);
    }

    private static bool HasPythonDocstring(
        string content,
        Match declaration)
    {
        var bodyStart = declaration.Index + declaration.Length;

        if (bodyStart >= content.Length)
        {
            return false;
        }

        foreach (var rawLine in content[bodyStart..].Split('\n').Take(8))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            return line.StartsWith("\"\"\"", StringComparison.Ordinal)
                || line.StartsWith("'''", StringComparison.Ordinal);
        }

        return false;
    }

    private static string[] GetExtensions(string language)
    {
        return language switch
        {
            "C#" => [".cs"],
            "Java" => [".java"],
            "Python" => [".py"],
            "TypeScript" => [".ts", ".tsx"],
            "JavaScript" => [".js", ".jsx"],
            _ => []
        };
    }

    private static bool IsEligibleSourceFile(string file)
    {
        var normalized = file.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);

        var parts = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Any(ExcludedDirectoryNames.Contains))
        {
            return false;
        }

        var filename = Path.GetFileName(file);

        return
            !filename.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            && !filename.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            && !filename.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            && !filename.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DeclarationCounts(
        int Total,
        int Documented);
}
