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
/// Evaluates language-level naming consistency and convention adherence.
/// Framework-specific naming rules are delegated to dedicated frontend evaluators.
///
/// Produces:
/// - NamingComplianceScore
/// - NamingHealthIndex
/// - Symbol violation metrics
///
/// Supported:
/// - C#
/// - Java
/// - Python
/// - TypeScript
/// - JavaScript
/// </summary>
public sealed class NamingEvaluator : BaseArchitectureEvaluator, IScopedDependency
{
    private readonly NamingPolicy _policy;
    private readonly NamingConventionResolver _resolver;


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



    private static readonly Regex TypeRegex =
        new(
            @"\b(class|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);



    private static readonly Regex MethodRegex =
        new(
            @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Compiled);



    private static readonly Regex VariableRegex =
        new(
            @"\b(var|let|const|int|float|double|string|bool|decimal)\s+([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);



    private static readonly Regex ConstantRegex =
        new(
            @"\b[A-Z][A-Z0-9_]{3,}\b",
            RegexOptions.Compiled);



    public NamingEvaluator(
        ILogger<NamingEvaluator> logger,
        IOptions<AegisArchitecturePolicy> options,
        NamingConventionResolver resolver)
        : base(logger)
    {
        _policy = options.Value.Naming ?? new NamingPolicy();
        _resolver = resolver;
    }



    protected override async Task<IEnumerable<ArchitectureEvaluatorResult>> EvaluateCoreAsync(
        string projectPath,
        CancellationToken token)
    {
        var results = new List<ArchitectureEvaluatorResult>();


        if (!_policy.Enabled)
        {
            _logger.LogInformation(
                "⏭ {Evaluator} disabled by policy.",
                Name);

            return results;
        }


        if (Context == null)
        {
            _logger.LogWarning(
                "⚠️ {Evaluator} skipped because architecture context is missing.",
                Name);

            return results;
        }



        var convention = _resolver.Resolve(Context);



        var files = Directory
            .EnumerateFiles(
                projectPath,
                "*.*",
                SearchOption.AllDirectories)
            .Where(IsSupportedFile)
            .Where(f => !IsExcludedDir(f))
            .ToList();



        _logger.LogInformation(
            "🏷️ Running {Evaluator} on {Count} files ({Language})",
            Name,
            files.Count,
            Context.Language);



        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();


            string content;

            try
            {
                content = await File.ReadAllTextAsync(
                    file,
                    token);
            }
            catch
            {
                continue;
            }



            results.Add(
                EvaluateFile(
                    file,
                    content,
                    convention));
        }



        if (results.Count > 0)
        {
            AddSummary(
                results,
                projectPath);
        }



        _logger.LogInformation(
            "✅ {Evaluator} completed with {Count} results",
            Name,
            results.Count);



        return results;
    }




    private ArchitectureEvaluatorResult EvaluateFile(
        string file,
        string content,
        INamingConventionPolicy convention)
    {
        var result =
            new ArchitectureEvaluatorResult(
                Name,
                file)
            {
                Category =
                    nameof(ArchitectureRuleCategory.Naming),

                Metrics =
                    new Dictionary<string, double>(),

                Metadata =
                    new Dictionary<string, string>
                    {
                        ["FileName"] =
                            Path.GetFileName(file),

                        ["Language"] =
                            Context?.Language ?? "Unknown"
                    }
            };



        var types =
            TypeRegex
                .Matches(content)
                .Cast<Match>()
                .Select(x => new
                {
                    Kind = x.Groups[1].Value,
                    Name = x.Groups[2].Value
                })
                .ToList();



        int typeViolations =
            types.Count(x =>
                !convention.IsValidTypeName(x.Name));



        int interfaceViolations =
            types.Count(x =>
                x.Kind == "interface" &&
                !convention.IsValidInterfaceName(x.Name));



        var methods =
            MethodRegex
                .Matches(content)
                .Cast<Match>()
                .Select(x =>
                    x.Groups[1].Value)
                .Where(IsMethodCandidate)
                .ToList();



        int methodViolations =
            methods.Count(x =>
                !convention.IsValidMethodName(x));



        var variables =
            VariableRegex
                .Matches(content)
                .Cast<Match>()
                .Select(x =>
                    x.Groups[2].Value)
                .ToList();



        int variableViolations =
            variables.Count(x =>
                !convention.IsValidVariableName(x));



        var constants =
            ConstantRegex
                .Matches(content)
                .Select(x => x.Value)
                .ToList();



        int constantViolations =
            constants.Count(x =>
                !convention.IsValidConstantName(x));



        double typeScore =
            ComputeScore(
                types.Count,
                typeViolations);



        double interfaceScore =
            ComputeScore(
                types.Count(x => x.Kind == "interface"),
                interfaceViolations);



        double methodScore =
            ComputeScore(
                methods.Count,
                methodViolations);



        double variableScore =
            ComputeScore(
                variables.Count,
                variableViolations);



        double constantScore =
            ComputeScore(
                constants.Count,
                constantViolations);



        double fileScore =
            convention.IsValidFileName(
                Path.GetFileNameWithoutExtension(file))
                ? 100
                : 0;



        double compliance =
            typeScore * 0.30 +
            interfaceScore * 0.15 +
            methodScore * 0.20 +
            variableScore * 0.15 +
            constantScore * 0.10 +
            fileScore * 0.10;



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


        result.Metrics["NamingComplianceScore"] =
            Math.Round(compliance, 2);



        result.Metadata["TypesAnalyzed"] =
            types.Count.ToString();

        result.Metadata["MethodsAnalyzed"] =
            methods.Count.ToString();

        result.Metadata["VariablesAnalyzed"] =
            variables.Count.ToString();



        return result;
    }





    private static void AddSummary(
        List<ArchitectureEvaluatorResult> results,
        string projectPath)
    {
        var score =
            results.Average(x =>
                x.Metrics.GetValueOrDefault(
                    "NamingComplianceScore",
                    0));



        var violations =
            results.Sum(x =>
                x.Metrics
                 .Where(m =>
                     m.Key.EndsWith(
                         "ViolationCount"))
                 .Sum(m => m.Value));



        results.Add(
            new ArchitectureEvaluatorResult(
                "NamingEvaluator",
                projectPath)
            {
                Category = "NamingSummary",

                Metrics =
                    new Dictionary<string, double>
                    {
                        ["AnalyzedFiles"] =
                            results.Count,

                        ["AverageNamingComplianceScore"] =
                            score,

                        ["TotalNamingViolations"] =
                            violations,

                        ["NamingHealthIndex"] =
                            score
                    },

                Metadata =
                    new Dictionary<string, string>
                    {
                        ["Evaluator"] =
                            "NamingEvaluator"
                    }
            });
    }





    private static double ComputeScore(
        int total,
        int violations)
    {
        if (total == 0)
            return 100;


        return Math.Round(
            Math.Max(
                0,
                100 -
                (violations * 100.0 / total)),
            2);
    }





    private static bool IsSupportedFile(string file)
    {
        return
            file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".java", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
    }





    private static bool IsMethodCandidate(string name)
    {
        return name switch
        {
            "if" or
            "for" or
            "foreach" or
            "while" or
            "switch" or
            "catch" or
            "return" => false,

            _ => true
        };
    }
}