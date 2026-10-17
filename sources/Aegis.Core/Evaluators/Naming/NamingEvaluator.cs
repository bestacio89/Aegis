
using System.Text.RegularExpressions;

using Aegis.Architecture.Evaluators;
using Aegis.Architecture.Evaluators.Naming.Policies;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Architecture.Models.Policies.Naming;

using Franz.Common.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aegis.Architecture.Evaluators.Naming;

/// <summary>
/// Evaluates repository source-file naming conventions and declaration-level
/// naming consistency.
///
/// Uses NamingConventionResolver as the authoritative source of naming rules.
/// Scores use a 0-100 scale.
///
/// This evaluator uses language-aware declaration heuristics, not compiler ASTs.
/// </summary>
public sealed class NamingEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly NamingPolicy _policy;
    private readonly NamingConventionResolver _resolver;

    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            ".idea",
            ".vscode",
            "bin",
            "obj",
            "node_modules",
            "dist",
            "build",
            "coverage",
            "vendor",
            "packages",
            "__pycache__",
            ".venv",
            "venv",
            "TestResults"
        };

    // Interfaces are scored separately and must not also inflate the type score.
    private static readonly Regex TypeDeclarationRx = new(
        """
        \b
        (?:
            class
            | enum
            | record
            | struct
            | trait
        )
        \s+
        (?:
            [A-Za-z_][A-Za-z0-9_]*
            \s+
        )?
        (?<name>[A-Za-z_][A-Za-z0-9_]*)
        """,
        RegexOptions.Compiled |
        RegexOptions.IgnorePatternWhitespace,
        TimeSpan.FromMilliseconds(500));

    // Lexical masking prevents declarations in comments and literals from
    // becoming findings. This is a safeguard, not a substitute for AST parsing.
    private static readonly Regex NonCodeRx = new(
        """"
        (?s)
        (?<literal>
            @?"(?:""|\\.|[^"\\])*"
            | '(?:\\.|[^'\\])*'
            | """.*?"""
            | '''.*?'''
            | \x60(?:\\.|[^\x60\\])*\x60
        )
        |
        (?<comment>
            //[^\x0D\x0A]*
            | /\*.*?\*/
            | \#[^\x0D\x0A]*
        )
        """",
        RegexOptions.Compiled |
        RegexOptions.IgnorePatternWhitespace,
        TimeSpan.FromMilliseconds(750));

    private static readonly Regex PythonClassRx = new(
        @"(?m)^\s*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\(|:)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex PythonFunctionRx = new(
        @"(?m)^\s*(?:async\s+)?def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex CStyleMethodRx = new(
        """
        (?mx)
        ^\s*
        (?:(?:public|private|protected|internal|static|virtual|override|
            abstract|async|extern|final|synchronized|native|default|
            export|declare|inline|constexpr|friend|explicit|operator|
            protected|readonly|unsafe|new)\s+)*
        (?:(?:[\w.<>,?\[\]&:*]+\s+)+)?
        (?<name>[A-Za-z_$][A-Za-z0-9_$]*)
        \s*
        \(
        """,
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(500));

    private static readonly Regex TypeScriptFunctionRx = new(
        """
        (?mx)
        ^\s*
        (?:(?:export|default|declare|async|abstract|static|public|private|
            protected|override|readonly|private|public)\s+)*
        (?:function\s*\*?\s+|(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*)
        (?:
            function\s*\*?\s+(?<functionName>[A-Za-z_$][A-Za-z0-9_$]*)
            |
            \(
        )
        """,
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(500));

    private static readonly Regex PythonVariableRx = new(
        @"(?m)^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::[^=\r\n]+)?=(?!=)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex CStyleVariableRx = new(
        """
        (?mx)
        \b
        (?:
            (?:const|let|var|final|static|readonly|volatile|register|auto)
            \s+
        )*
        (?:
            (?:int|long|short|byte|float|double|decimal|bool|boolean|
                char|string|String|var|Object|object|const|final)
            |
            [A-Z][A-Za-z0-9_<>,?.\[\]]*
        )
        \s+
        (?<name>[A-Za-z_$][A-Za-z0-9_$]*)
        \s*
        (?=[=;,:)\[])
        """,
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(500));

    private static readonly Regex JavaScriptVariableRx = new(
        @"\b(?:const|let|var)\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex ConstantDeclarationRx = new(
        """
        (?mx)
        \b
        (?:
            const
            | final
            | readonly
        )
        \s+
        (?:(?:[\w.<>,?\[\]]+)\s+)?
        (?<name>[A-Z][A-Z0-9_]{2,})
        \s*(?:=|;|,)
        """,
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(500));

    private static readonly Regex PythonConstantDeclarationRx = new(
        @"(?m)^\s*(?<name>[A-Z][A-Z0-9_]{2,})\s*=",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex InterfaceDeclarationRx = new(
        @"\binterface\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex CommentOnlyLineRx = new(
        @"(?m)^\s*(?://|#|/\*|\*|\*/)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    public override string Name => "NamingEvaluator";

    public override string[] SupportedLanguages =>
    [
        "C#",
        "Java",
        "Python",
        "TypeScript",
        "JavaScript"
    ];

    public override string[] SupportedFrameworks =>
    [
        "LanguageAgnostic"
    ];

    public NamingEvaluator(
        ILogger<NamingEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options,
        NamingConventionResolver resolver)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolver);

        _policy = options.Value.Naming ?? new NamingPolicy();
        _resolver = resolver;
    }

    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>>
        EvaluateCoreAsync(
            string projectPath,
            CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        token.ThrowIfCancellationRequested();

        if (!_policy.Enabled)
        {
            _logger.LogInformation(
                "{Evaluator} disabled by policy.",
                Name);

            return [];
        }

        if (Context is null)
        {
            _logger.LogWarning(
                "{Evaluator} skipped because architecture context is missing.",
                Name);

            return [];
        }

        if (!Directory.Exists(projectPath))
        {
            _logger.LogWarning(
                "{Evaluator} skipped because project directory does not exist: {Path}",
                Name,
                projectPath);

            return [];
        }

        var files = EnumerateSourceFiles(projectPath, token)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file, StringComparer.Ordinal)
            .ToList();

        _logger.LogInformation(
            "Running {Evaluator} on {Count} source files for language {Language}.",
            Name,
            files.Count,
            Context.Language);

        var results = new List<ArchitectureEvaluatorResult>();
        var unreadableFiles = 0;

        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();

            string content;

            try
            {
                content = await File.ReadAllTextAsync(file, token);
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (IOException exception)
            {
                unreadableFiles++;

                _logger.LogDebug(
                    exception,
                    "Unable to read source file {File}.",
                    file);

                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                unreadableFiles++;

                _logger.LogDebug(
                    exception,
                    "Access denied while reading source file {File}.",
                    file);

                continue;
            }

            var language = DetectLanguage(file);
            var fileContext = new ProjectArchitectureContext(
                language,
                string.Equals(language, Context.Language, StringComparison.OrdinalIgnoreCase)
                    ? Context.Framework
                    : null,
                Context.Confidence);

            results.Add(EvaluateFile(
                file,
                content,
                _resolver.Resolve(fileContext)));
        }

        var analyzedFileCount = results.Count;

        var summary = CreateSummary(
            results,
            projectPath,
            files.Count,
            unreadableFiles);

        results.Add(summary);

        _logger.LogInformation(
            "{Evaluator} completed. Source files discovered: {Discovered}; analyzed: {Analyzed}; unreadable: {Unreadable}.",
            Name,
            files.Count,
            analyzedFileCount,
            unreadableFiles);

        return results;
    }

    private ArchitectureEvaluatorResult EvaluateFile(
        string file,
        string content,
        INamingConventionPolicy convention)
    {
        var result = CreateResult(
            file,
            nameof(ArchitectureRuleCategory.Naming));

        var language = DetectLanguage(file);

        result.Metadata["FileName"] = Path.GetFileName(file);
        result.Metadata["Language"] = language;
        result.Metadata["AnalysisMethod"] = "LanguageAwareDeclarationHeuristics";

        var analysisContent = MaskNonCode(content);
        result.Metadata["AnalysisMethod"] = "LexicallyMaskedDeclarationHeuristics";

        var typeNames = ExtractTypeNames(analysisContent, language);

        var typeViolations = typeNames.Count(
            name => !convention.IsValidTypeName(name));

        var interfaceNames = InterfaceDeclarationRx
            .Matches(analysisContent)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var interfaceViolations = interfaceNames.Count(
            name => !convention.IsValidInterfaceName(name));

        var methodNames = ExtractMethodNames(analysisContent, language);

        var methodViolations = methodNames.Count(
            name => !convention.IsValidMethodName(name));

        var variableNames = ExtractVariableNames(analysisContent, language);

        var variableViolations = variableNames.Count(
            name => !convention.IsValidVariableName(name));

        var constantNames = ExtractConstantNames(analysisContent, language);

        var constantViolations = constantNames.Count(
            name => !convention.IsValidConstantName(name));

        var typeScore = ComputeScore(typeNames.Count, typeViolations);
        var interfaceScore = ComputeScore(
            interfaceNames.Length,
            interfaceViolations);
        var methodScore = ComputeScore(methodNames.Count, methodViolations);
        var variableScore = ComputeScore(
            variableNames.Count,
            variableViolations);
        var constantScore = ComputeScore(
            constantNames.Count,
            constantViolations);

        var fileScore = convention.IsValidFileName(
            Path.GetFileNameWithoutExtension(file))
            ? 100
            : 0;

        var declarationCount =
            typeNames.Count +
            interfaceNames.Length +
            methodNames.Count +
            variableNames.Count +
            constantNames.Count;

        // Only dimensions with observed declarations contribute to the score.
        // An empty file has no declaration-compliance score; file-name compliance
        // remains independently visible as its own metric.
        var weightedScore = 0d;
        var activeWeight = 0d;

        AddDimension(typeNames.Count, typeScore, 0.30, ref weightedScore, ref activeWeight);
        AddDimension(interfaceNames.Length, interfaceScore, 0.15, ref weightedScore, ref activeWeight);
        AddDimension(methodNames.Count, methodScore, 0.20, ref weightedScore, ref activeWeight);
        AddDimension(variableNames.Count, variableScore, 0.15, ref weightedScore, ref activeWeight);
        AddDimension(constantNames.Count, constantScore, 0.10, ref weightedScore, ref activeWeight);

        // File naming is applicable to every discovered source file.
        weightedScore += fileScore * 0.10;
        activeWeight += 0.10;

        var compliance = declarationCount == 0
            ? 0
            : Math.Round(weightedScore / activeWeight, 2);

        result.Metrics["TypeNamingCompliance"] = typeScore;
        result.Metrics["InterfaceNamingCompliance"] = interfaceScore;
        result.Metrics["MethodNamingCompliance"] = methodScore;
        result.Metrics["VariableNamingCompliance"] = variableScore;
        result.Metrics["ConstantNamingCompliance"] = constantScore;
        result.Metrics["FileNamingCompliance"] = fileScore;

        result.Metrics["TypeViolationCount"] = typeViolations;
        result.Metrics["InterfaceViolationCount"] = interfaceViolations;
        result.Metrics["MethodViolationCount"] = methodViolations;
        result.Metrics["VariableViolationCount"] = variableViolations;
        result.Metrics["ConstantViolationCount"] = constantViolations;
        result.Metrics["DeclarationsAnalyzed"] = declarationCount;
        result.Metrics["NamingEvidenceAvailable"] = declarationCount > 0 ? 1 : 0;
        result.Metadata["EvaluationStatus"] = declarationCount == 0
            ? "NoDeclarations"
            : "Evaluated";

        result.Metrics["NamingComplianceScore"] =
            Math.Round(compliance, 2);

        result.Metadata["TypesAnalyzed"] = typeNames.Count.ToString();
        result.Metadata["InterfacesAnalyzed"] = interfaceNames.Length.ToString();
        result.Metadata["MethodsAnalyzed"] = methodNames.Count.ToString();
        result.Metadata["VariablesAnalyzed"] = variableNames.Count.ToString();
        result.Metadata["ConstantsAnalyzed"] = constantNames.Count.ToString();

        return result;
    }

    private ArchitectureEvaluatorResult CreateSummary(
        IReadOnlyCollection<ArchitectureEvaluatorResult> fileResults,
        string projectPath,
        int discoveredFileCount,
        int unreadableFileCount)
    {
        var totalDeclarations = fileResults.Sum(result =>
            result.Metrics.GetValueOrDefault("DeclarationsAnalyzed", 0));

        var averageScore = totalDeclarations == 0
            ? 0
            : fileResults
                .Where(result => result.Metrics.GetValueOrDefault("DeclarationsAnalyzed", 0) > 0)
                .Average(result => result.Metrics.GetValueOrDefault("NamingComplianceScore", 0));

        var totalViolations = fileResults.Sum(result =>
            result.Metrics
                .Where(metric =>
                    metric.Key.EndsWith(
                        "ViolationCount",
                        StringComparison.Ordinal))
                .Sum(metric => metric.Value));

        return new ArchitectureEvaluatorResult(Name, projectPath)
        {
            Category = "NamingSummary",

            Metrics =
            {
                ["AnalyzedFiles"] = fileResults.Count,
                ["DiscoveredFiles"] = discoveredFileCount,
                ["UnreadableFiles"] = unreadableFileCount,
                ["AverageNamingComplianceScore"] = Math.Round(averageScore, 2),
                ["TotalNamingViolations"] = totalViolations,
                ["DeclarationsAnalyzed"] = totalDeclarations,
                ["NamingHealthIndex"] = Math.Round(averageScore, 2)
            },

            Metadata =
            {
                ["Evaluator"] = Name,
                ["ProjectPath"] = projectPath,
                ["EvaluationStatus"] = discoveredFileCount == 0
                    ? "NoApplicableFiles"
                    : fileResults.Count == 0
                        ? "AnalysisFailed"
                        : unreadableFileCount > 0
                            ? "PartiallyEvaluated"
                            : totalDeclarations == 0
                                ? "NoDeclarations"
                                : "Evaluated"
            }
        };
    }

    private static string MaskNonCode(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return NonCodeRx.Replace(content, match =>
            string.Create(
                match.Length,
                match.Value,
                static (span, value) =>
                {
                    for (var index = 0; index < value.Length; index++)
                    {
                        span[index] = value[index] is '\r' or '\n'
                            ? value[index]
                            : ' ';
                    }
                }));
    }

    private static List<string> ExtractTypeNames(
        string content,
        string language)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in TypeDeclarationRx.Matches(content))
        {
            var name = match.Groups["name"].Value;

            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        if (language == "Python")
        {
            foreach (Match match in PythonClassRx.Matches(content))
            {
                names.Add(match.Groups["name"].Value);
            }
        }

        return names.ToList();
    }

    private static List<string> ExtractMethodNames(
        string content,
        string language)
    {
        var names = new List<string>();

        if (language == "Python")
        {
            names.AddRange(
                PythonFunctionRx.Matches(content)
                    .Select(match => match.Groups["name"].Value));

            return names;
        }

        foreach (Match match in CStyleMethodRx.Matches(content))
        {
            var name = match.Groups["name"].Value;

            if (IsMethodCandidate(name))
            {
                names.Add(name);
            }
        }

        if (language is "TypeScript" or "JavaScript")
        {
            foreach (Match match in TypeScriptFunctionRx.Matches(content))
            {
                var name = match.Groups["functionName"].Success
                    ? match.Groups["functionName"].Value
                    : match.Groups["name"].Value;

                if (!string.IsNullOrWhiteSpace(name) &&
                    IsMethodCandidate(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private static List<string> ExtractVariableNames(
        string content,
        string language)
    {
        var names = new List<string>();

        if (language is "JavaScript" or "TypeScript")
        {
            names.AddRange(
                JavaScriptVariableRx.Matches(content)
                    .Select(match => match.Groups["name"].Value));

            return names;
        }

        if (language == "Python")
        {
            names.AddRange(
                PythonVariableRx.Matches(content)
                    .Select(match => match.Groups["name"].Value));

            return names;
        }

        names.AddRange(
            CStyleVariableRx.Matches(content)
                .Select(match => match.Groups["name"].Value));

        return names;
    }

    private static List<string> ExtractConstantNames(
        string content,
        string language)
    {
        var regex = language == "Python"
            ? PythonConstantDeclarationRx
            : ConstantDeclarationRx;

        return regex.Matches(content)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void AddDimension(
        int count,
        double score,
        double weight,
        ref double weightedScore,
        ref double activeWeight)
    {
        if (count <= 0)
        {
            return;
        }

        weightedScore += score * weight;
        activeWeight += weight;
    }

    private static double ComputeScore(int total, int violations)
    {
        if (total == 0)
        {
            return 0;
        }

        return Math.Round(
            Math.Max(0, 100 - violations * 100.0 / total),
            2);
    }

    private static List<string> EnumerateSourceFiles(
        string projectPath,
        CancellationToken token)
    {
        var files = new List<string>();
        var pendingDirectories = new Stack<string>();

        pendingDirectories.Push(Path.GetFullPath(projectPath));

        while (pendingDirectories.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var current = pendingDirectories.Pop();

            IEnumerable<string> directories;

            try
            {
                directories = Directory.EnumerateDirectories(current).ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                token.ThrowIfCancellationRequested();

                var name = Path.GetFileName(
                    Path.TrimEndingDirectorySeparator(directory));

                if (ExcludedDirectories.Contains(name))
                {
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(directory) &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                pendingDirectories.Push(directory);
            }

            try
            {
                files.AddRange(
                    Directory.EnumerateFiles(current, "*")
                        .Where(IsSupportedFile));
            }
            catch (IOException)
            {
                // Preserve results from readable directories.
            }
            catch (UnauthorizedAccessException)
            {
                // Preserve results from readable directories.
            }
        }

        return files;
    }

    private static bool IsSupportedFile(string file)
    {
        return Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".cs" => true,
            ".java" => true,
            ".py" => true,
            ".ts" => true,
            ".tsx" => true,
            ".js" => true,
            ".jsx" => true,
            _ => false
        };
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

    private static bool IsMethodCandidate(string name)
    {
        return name is not (
            "if" or "for" or "foreach" or "while" or "switch" or
            "catch" or "return" or "throw" or "new" or "using" or
            "lock" or "sizeof" or "typeof" or "nameof" or "await" or
            "yield" or "super" or "this" or "base" or "function" or
            "def" or "class" or "interface" or "enum" or "record" or
            "struct" or "do" or "try" or "finally" or "else" or
            "elif" or "except" or "with" or "assert" or "del" or
            "pass" or "lambda" or "async" or "from" or "import");
    }
}
