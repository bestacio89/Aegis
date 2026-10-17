namespace Aegis.Architecture.Evaluators.Naming.Analysis;

/// <summary>Analysis outcome with explicit coverage and parser diagnostics.</summary>
public sealed record NamingAnalysisResult(
    string FilePath,
    string Language,
    string? Framework,
    string AnalyzerId,
    NamingAnalysisStatus Status,
    IReadOnlyList<NamingDeclaration> Declarations,
    IReadOnlyList<NamingAnalysisDiagnostic> Diagnostics,
    int SourceLineCount)
{
    public bool HasDeclarationEvidence => Declarations.Count > 0;

    public bool IsComplete =>
        Status is NamingAnalysisStatus.Evaluated or NamingAnalysisStatus.NoDeclarations;
}
