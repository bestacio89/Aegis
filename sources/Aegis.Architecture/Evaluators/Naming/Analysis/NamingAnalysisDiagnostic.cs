namespace Aegis.Architecture.Evaluators.Naming.Analysis;

/// <summary>Parser/analyzer diagnostic; never treat it as a naming violation.</summary>
public sealed record NamingAnalysisDiagnostic(
    string Code,
    string Message,
    string FilePath,
    int? Line,
    int? Column);
