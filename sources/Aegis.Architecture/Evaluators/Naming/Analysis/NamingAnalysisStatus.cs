namespace Aegis.Architecture.Evaluators.Naming.Analysis;

/// <summary>Completeness of a single source-file analysis.</summary>
public enum NamingAnalysisStatus
{
    Evaluated,
    NoDeclarations,
    PartiallyEvaluated,
    AnalysisFailed,
    UnsupportedLanguage
}
