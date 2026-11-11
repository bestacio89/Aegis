using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Aegis.Architecture.Diagnostics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Diagnostics;
using Franz.Common.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aegis.Architecture.Evaluators.Architecture;

/// <summary>
/// Measures documentation coverage independently for each discovered project.
/// Evidence retains the source file and declaration line for both documented
/// and undocumented public declarations. The measurement remains a source
/// pattern heuristic rather than compiler-backed API analysis.
/// </summary>
public sealed class DocumentationEvaluator :
    BaseArchitectureEvaluator,
    IScopedDependency
{
    private static readonly RegexOptions SourceRegexOptions =
        RegexOptions.Compiled
        | RegexOptions.Multiline
        | RegexOptions.CultureInvariant;

    private static readonly Regex JavaDeclarationRegex = new(
        @"^[ \t]*public\b[^\r\n]*",
        SourceRegexOptions);

    private static readonly Regex TypeScriptDeclarationRegex = new(
        @"^[ \t]*(?:(?:export|declare)\s+)*(?:(?:abstract|async|default)\s+)*(?:class|interface|type|function|enum|namespace)\s+\w+|^[ \t]*export\s+(?:declare\s+)?(?:const|let|var)\s+\w+",
        SourceRegexOptions);

    private static readonly Regex PythonDeclarationRegex = new(
        @"^[ \t]*(?:async\s+)?(?:def|class)\s+([A-Za-z]\w*)\s*(?:\([^:\r\n]*\))?[^:\r\n]*:",
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

    private static readonly string[] SupportedExtensions =
    [
        ".cs", ".java", ".py", ".ts", ".tsx", ".js", ".jsx"
    ];

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

    public DocumentationEvaluator(ILogger<DocumentationEvaluator> logger)
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
            return [];

        var files = ResolveSourceFiles(projectPath, SupportedExtensions)
            .Where(IsEligibleSourceFile)
            .ToList();

        var scopes = BuildProjectScopes(projectPath, files);
        var results = new List<ArchitectureEvaluatorResult>();

        foreach (var scope in scopes)
        {
            token.ThrowIfCancellationRequested();

            var totalDeclarations = 0;
            var documentedDeclarations = 0;
            var analyzedFiles = 0;
            var unreadableFiles = 0;
            var incompleteFiles = new List<string>();
            var analysisDiagnostics = new List<string>();
            var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var evidence = new List<string>();

            foreach (var file in scope.Files)
            {
                token.ThrowIfCancellationRequested();

                string source;

                try
                {
                    source = await File.ReadAllTextAsync(file, token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (IOException exception)
                {
                    unreadableFiles++;
                    _logger.LogWarning(
                        exception,
                        "Could not read source file {SourceFile} during documentation analysis.",
                        file);
                    continue;
                }
                catch (UnauthorizedAccessException exception)
                {
                    unreadableFiles++;
                    _logger.LogWarning(
                        exception,
                        "Access denied while reading source file {SourceFile} during documentation analysis.",
                        file);
                    continue;
                }

                token.ThrowIfCancellationRequested();

                var language = DetectLanguage(file);
                if (language == "Unknown")
                    continue;

                languages.Add(language);

                var counts = CountDeclarations(
                    source,
                    language,
                    Path.GetRelativePath(scope.Path, file));

                totalDeclarations += counts.Total;
                documentedDeclarations += counts.Documented;
                analyzedFiles++;
                evidence.AddRange(counts.Evidence);

                if (!counts.IsComplete)
                {
                    incompleteFiles.Add(file);
                    analysisDiagnostics.AddRange(counts.Diagnostics ?? []);
                }
            }

            if (unreadableFiles > 0 || incompleteFiles.Count > 0)
            {
                var status = CreateExecutionStatusFact(
                    scope.Path,
                    "Incomplete",
                    $"Documentation analysis could not fully analyze {unreadableFiles} unreadable and {incompleteFiles.Count} syntactically incomplete source file(s).");

                ApplyProjectIdentity(status, scope);
                status.Metadata["AnalysisComplete"] = "false";
                status.Metadata["UnreadableFiles"] = unreadableFiles.ToString();
                status.Metadata["IncompleteFiles"] = incompleteFiles.Count.ToString();
                status.Metadata["FilesAnalyzed"] = analyzedFiles.ToString();
                status.Metadata["AnalysisDiagnosticCount"] = analysisDiagnostics.Count.ToString();
                status.Metadata["AnalysisDiagnostics"] = string.Join(
                    Environment.NewLine,
                    analysisDiagnostics.Take(20));
                results.Add(status);
                continue;
            }

            if (totalDeclarations == 0)
            {
                var status = CreateExecutionStatusFact(
                    scope.Path,
                    "NoApplicableDeclarations",
                    "No eligible public declarations were discovered in this project scope.");

                ApplyProjectIdentity(status, scope);
                status.Metadata["AnalysisComplete"] = "true";
                status.Metadata["FilesAnalyzed"] = analyzedFiles.ToString();
                results.Add(status);
                continue;
            }

            var coverage = documentedDeclarations * 100.0 / totalDeclarations;
            var summary = CreateProjectSummary(
                scope,
                coverage,
                totalDeclarations,
                documentedDeclarations,
                analyzedFiles,
                evidence,
                languages);

            results.Add(summary);

            AegisDiagnostics.Report(
                Name,
                DiagnosticLevel.Info,
                $"Documentation analysis completed for {scope.Name}. " +
                $"Coverage={coverage:0.0}%; documented={documentedDeclarations}; " +
                $"declarations={totalDeclarations}; files={analyzedFiles}.");
        }

        return results;
    }

    private IReadOnlyList<DocumentationProjectScope> BuildProjectScopes(
        string projectPath,
        IReadOnlyCollection<string> files)
    {
        if (Context is null || Context.Modules.Count == 0)
        {
            return files.Count == 0
                ? []
                : [new DocumentationProjectScope(
                    Context?.ProjectName ?? Path.GetFileName(projectPath),
                    Path.GetFullPath(projectPath),
                    "Unknown",
                    Context?.Framework,
                    files.ToArray())];
        }

        var allModules = Context.Modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Path))
            .OrderByDescending(module => NormalizePath(module.Path).Length)
            .ToArray();

        var modules = allModules
            .Where(module => !module.IsTestProject
                && !string.Equals(
                    module.ProjectKind,
                    "Test",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var scopes = new List<DocumentationProjectScope>();

        foreach (var module in modules)
        {
            var ownedFiles = files
                .Where(file => IsSameOrDescendantPath(file, module.Path))
                .Where(file =>
                {
                    var owner = allModules.FirstOrDefault(candidate =>
                        IsSameOrDescendantPath(file, candidate.Path));
                    return ReferenceEquals(owner, module);
                })
                .ToArray();

            if (ownedFiles.Length == 0)
                continue;

            scopes.Add(new DocumentationProjectScope(
                module.Name,
                module.Path,
                module.ProjectKind,
                module.Framework,
                ownedFiles));
        }

        var unownedFiles = files
            .Where(file => !allModules.Any(module =>
                IsSameOrDescendantPath(file, module.Path)))
            .ToArray();

        if (unownedFiles.Length > 0)
        {
            scopes.Add(new DocumentationProjectScope(
                Context.ProjectName,
                Path.GetFullPath(Context.RootPath),
                "Unknown",
                Context.Framework,
                unownedFiles));
        }

        return scopes;
    }

    private ArchitectureEvaluatorResult CreateProjectSummary(
        DocumentationProjectScope scope,
        double coverage,
        int totalDeclarations,
        int documentedDeclarations,
        int analyzedFiles,
        IReadOnlyCollection<string> evidence,
        IReadOnlyCollection<string> languages)
    {
        var evidenceItems = evidence
            .Take(100)
            .ToArray();

        var result = new ArchitectureEvaluatorResult(Name, scope.Name)
        {
            ProjectName = scope.Name,
            Category = nameof(ArchitectureRuleCategory.Architecture),
            Metrics = new Dictionary<string, double>
            {
                ["DocumentationCoverage"] = Math.Round(coverage, 2),
                ["DocumentationCompliance"] = Math.Round(coverage, 2),
                ["TotalDeclarations"] = totalDeclarations,
                ["DocumentedDeclarations"] = documentedDeclarations,
                ["MissingDocumentation"] = totalDeclarations - documentedDeclarations,
                ["DocumentationAnalysisFileCount"] = analyzedFiles,
                ["DocumentationDiscoveredFileCount"] = scope.Files.Count
            },
            Evidence = string.Join(Environment.NewLine, evidenceItems),
            Metadata = new Dictionary<string, string>
            {
                [EvaluationScopeMetadataKey] = ProjectScope,
                ["MetricScope"] = ProjectScope,
                ["ProjectName"] = scope.Name,
                ["ProjectPath"] = scope.Path,
                ["ProjectKind"] = scope.ProjectKind,
                ["Language"] = languages.Count == 1
                    ? languages.Single()
                    : languages.Count == 0 ? "Unknown" : "Multi-Language",
                ["Framework"] = scope.Framework ?? "Unknown",
                ["MeasurementMethod"] = "LanguageSpecificSourceHeuristics",
                ["CoverageUnit"] = "Percent",
                ["AnalysisStatus"] = "Completed",
                ["AnalysisComplete"] = "true",
                ["Evidence.DocumentationCoverage"] =
                    string.Join(Environment.NewLine, evidenceItems),
                ["EvidenceSummary.DocumentationCoverage"] =
                    $"{documentedDeclarations} of {totalDeclarations} public declarations have recognized documentation.",
                ["EvidenceTruncated"] =
                    (evidence.Count > evidenceItems.Length).ToString()
            }
        };

        return result;
    }

    private static void ApplyProjectIdentity(
        ArchitectureEvaluatorResult result,
        DocumentationProjectScope scope)
    {
        result.ProjectName = scope.Name;
        result.Target = scope.Name;
        result.Metadata["ProjectName"] = scope.Name;
        result.Metadata["ProjectPath"] = scope.Path;
        result.Metadata["ProjectKind"] = scope.ProjectKind;
        result.Metadata["Framework"] = scope.Framework ?? "Unknown";
        result.Metadata["EvaluationScope"] = ProjectScope;
    }

    private static DeclarationCounts CountDeclarations(
        string content,
        string language,
        string relativePath)
    {
        return language switch
        {
            "C#" => CountCSharpDeclarations(content, relativePath),

            "Java" => CountCommentDocumentedDeclarations(
                content,
                relativePath,
                JavaDeclarationRegex,
                null),

            "TypeScript" or "JavaScript" =>
                CountCommentDocumentedDeclarations(
                    content,
                    relativePath,
                    TypeScriptDeclarationRegex,
                    null),

            "Python" => CountPythonDeclarations(content, relativePath),

            _ => new DeclarationCounts(0, 0, [])
        };
    }

    private static DeclarationCounts CountCSharpDeclarations(
        string content,
        string relativePath)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            content,
            new CSharpParseOptions(LanguageVersion.Preview));
        var root = syntaxTree.GetRoot();

        var declarations = root.DescendantNodes()
            .Where(node =>
                node is EnumMemberDeclarationSyntax
                || node is MemberDeclarationSyntax member
                    && IsPublicCSharpDeclaration(member))
            .ToArray();

        var documented = 0;
        var evidence = new List<string>(declarations.Length);

        foreach (var declaration in declarations)
        {
            var isDocumented = declaration.GetLeadingTrivia().Any(trivia =>
                trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

            if (isDocumented)
                documented++;

            evidence.Add(FormatDeclarationEvidence(
                content,
                relativePath,
                declaration.SpanStart,
                isDocumented));
        }

        var diagnostics = syntaxTree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(20)
            .Select(diagnostic =>
            {
                var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
                return $"Line {line}: {diagnostic.GetMessage()}";
            })
            .ToArray();

        return new DeclarationCounts(
            declarations.Length,
            documented,
            evidence,
            diagnostics.Length == 0,
            diagnostics);
    }

    private static bool IsPublicCSharpDeclaration(
        MemberDeclarationSyntax declaration)
    {
        if (declaration is BaseTypeDeclarationSyntax
            or DelegateDeclarationSyntax)
        {
            return HasPublicModifier(declaration);
        }

        if (HasPublicModifier(declaration))
            return true;

        // Interface members are public by default unless explicitly private,
        // protected or internal. Explicit access modifiers take precedence.
        return declaration.Parent is InterfaceDeclarationSyntax
            && !HasNonPublicModifier(declaration);
    }

    private static bool HasPublicModifier(MemberDeclarationSyntax declaration) =>
        declaration.ChildTokens().Any(token =>
            token.IsKind(SyntaxKind.PublicKeyword));

    private static bool HasNonPublicModifier(MemberDeclarationSyntax declaration) =>
        declaration.ChildTokens().Any(token =>
            token.IsKind(SyntaxKind.PrivateKeyword)
            || token.IsKind(SyntaxKind.ProtectedKeyword)
            || token.IsKind(SyntaxKind.InternalKeyword));

    private static DeclarationCounts CountCommentDocumentedDeclarations(
        string content,
        string relativePath,
        Regex declarationRegex,
        Regex? documentationRegex)
    {
        var declarations = declarationRegex.Matches(content);
        var documentedCount = 0;
        var evidence = new List<string>();

        foreach (Match declaration in declarations)
        {
            var isDocumented = HasPrecedingDocumentation(
                content,
                declaration.Index,
                documentationRegex);

            if (isDocumented)
                documentedCount++;

            evidence.Add(FormatDeclarationEvidence(
                content,
                relativePath,
                declaration.Index,
                isDocumented));
        }

        return new DeclarationCounts(
            declarations.Count,
            documentedCount,
            evidence);
    }

    private static bool HasPrecedingDocumentation(
        string content,
        int declarationIndex,
        Regex? documentationRegex)
    {
        var prefix = content[..declarationIndex].TrimEnd();
        if (prefix.Length == 0)
            return false;

        if (documentationRegex is not null)
        {
            var lastLineStart = prefix.LastIndexOf('\n');
            var lastLine = prefix[(lastLineStart + 1)..];
            return documentationRegex.IsMatch(lastLine);
        }

        var lastClose = prefix.LastIndexOf("*/", StringComparison.Ordinal);
        if (lastClose < 0)
            return false;

        var lastOpen = prefix.LastIndexOf(
            "/**",
            lastClose,
            StringComparison.Ordinal);

        return lastOpen >= 0
            && string.IsNullOrWhiteSpace(prefix[(lastClose + 2)..]);
    }

    private static DeclarationCounts CountPythonDeclarations(
        string content,
        string relativePath)
    {
        var declarations = PythonDeclarationRegex.Matches(content)
            .Cast<Match>()
            .Where(declaration =>
                !declaration.Groups[1].Value.StartsWith(
                    "_",
                    StringComparison.Ordinal))
            .ToArray();

        var documentedCount = 0;
        var evidence = new List<string>();

        foreach (var declaration in declarations)
        {
            var isDocumented = HasPythonDocstring(content, declaration);
            if (isDocumented)
                documentedCount++;

            evidence.Add(FormatDeclarationEvidence(
                content,
                relativePath,
                declaration.Index,
                isDocumented));
        }

        return new DeclarationCounts(
            declarations.Length,
            documentedCount,
            evidence);
    }

    private static string FormatDeclarationEvidence(
        string content,
        string relativePath,
        int declarationIndex,
        bool documented)
    {
        var lineNumber = GetLineNumber(content, declarationIndex);
        var line = GetSourceLine(content, declarationIndex);

        return $"{relativePath}:{lineNumber}: " +
            $"{(documented ? "documented" : "missing documentation")} — {line}";
    }

    private static int GetLineNumber(string content, int position)
    {
        var lineNumber = 1;
        for (var index = 0; index < position; index++)
        {
            if (content[index] == '\n')
                lineNumber++;
        }

        return lineNumber;
    }

    private static string GetSourceLine(string content, int position)
    {
        var start = content.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
        var end = content.IndexOf('\n', position);
        if (end < 0)
            end = content.Length;

        return content[start..end].Trim();
    }

    private static bool HasPythonDocstring(
        string content,
        Match declaration)
    {
        var bodyStart = declaration.Index + declaration.Length;
        if (bodyStart >= content.Length)
            return false;

        foreach (var rawLine in content[bodyStart..].Split('\n').Take(8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            return line.StartsWith("\"\"\"", StringComparison.Ordinal)
                || line.StartsWith("'''", StringComparison.Ordinal);
        }

        return false;
    }

    private static string DetectLanguage(string file)
    {
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".java" => "Java",
            ".py" => "Python",
            ".ts" or ".tsx" => "TypeScript",
            ".js" or ".jsx" => "JavaScript",
            _ => "Unknown"
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
            return false;

        var filename = Path.GetFileName(file);

        return !filename.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            && !filename.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            && !filename.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            && !filename.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrDescendantPath(string candidate, string parent)
    {
        var normalizedCandidate = NormalizePath(candidate);
        var normalizedParent = NormalizePath(parent);

        return string.Equals(
                normalizedCandidate,
                normalizedParent,
                StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

    private sealed record DocumentationProjectScope(
        string Name,
        string Path,
        string ProjectKind,
        string? Framework,
        IReadOnlyCollection<string> Files);

    private sealed record DeclarationCounts(
        int Total,
        int Documented,
        IReadOnlyCollection<string> Evidence,
        bool IsComplete = true,
        IReadOnlyCollection<string>? Diagnostics = null);
}
