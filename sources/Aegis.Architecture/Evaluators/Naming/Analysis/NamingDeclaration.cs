namespace Aegis.Architecture.Evaluators.Naming.Analysis;

/// <summary>One syntax-backed declaration. Source positions are one-based.</summary>
public sealed record NamingDeclaration(
    string Name,
    NamingDeclarationKind Kind,
    string FilePath,
    int StartLine,
    int StartColumn,
    string Language,
    string? Framework,
    string AnalyzerId,
    bool IsPublic,
    bool IsStatic,
    bool IsConstant);
