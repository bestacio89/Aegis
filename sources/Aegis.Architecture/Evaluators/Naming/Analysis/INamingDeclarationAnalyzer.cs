namespace Aegis.Architecture.Evaluators.Naming.Analysis;

/// <summary>
/// Language-specific declaration discovery. Implementations should parse syntax
/// without requiring project compilation unless their language requires it.
/// </summary>
public interface INamingDeclarationAnalyzer
{
    string AnalyzerId { get; }
    string Language { get; }

    bool CanAnalyze(string filePath, string language);

    ValueTask<NamingAnalysisResult> AnalyzeAsync(
        string filePath,
        string source,
        string? framework,
        CancellationToken cancellationToken = default);
}
