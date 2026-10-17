using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aegis.Architecture.Evaluators.Naming.Analysis;

/// <summary>
/// Roslyn-backed C# declaration discovery. This component discovers declarations;
/// policy validation, diagnostics presentation and scoring remain separate.
/// </summary>
public sealed class CSharpNamingDeclarationAnalyzer : INamingDeclarationAnalyzer
{
    public const string Id = "Roslyn.CSharp";
    public string AnalyzerId => Id;
    public string Language => "C#";

    public bool CanAnalyze(string filePath, string language) =>
        string.Equals(language, Language, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetExtension(filePath), ".cs",
            StringComparison.OrdinalIgnoreCase);

    public ValueTask<NamingAnalysisResult> AnalyzeAsync(
        string filePath,
        string source,
        string? framework,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest),
            path: filePath,
            cancellationToken: cancellationToken);

        var root = tree.GetRoot(cancellationToken);
        var declarations = new List<NamingDeclaration>();
        var diagnostics = new List<NamingAnalysisDiagnostic>();

        foreach (var diagnostic in tree.GetDiagnostics(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostic.Severity != DiagnosticSeverity.Error)
                continue;

            var location = diagnostic.Location;
            var lineSpan = location.IsInSource ? location.GetLineSpan() : default;
            diagnostics.Add(new NamingAnalysisDiagnostic(
                diagnostic.Id,
                diagnostic.GetMessage(),
                filePath,
                location.IsInSource ? lineSpan.StartLinePosition.Line + 1 : null,
                location.IsInSource ? lineSpan.StartLinePosition.Character + 1 : null));
        }

        new DeclarationWalker(filePath, framework, declarations, cancellationToken)
            .Visit(root);

        var status = diagnostics.Count > 0
            ? declarations.Count > 0
                ? NamingAnalysisStatus.PartiallyEvaluated
                : NamingAnalysisStatus.AnalysisFailed
            : declarations.Count == 0
                ? NamingAnalysisStatus.NoDeclarations
                : NamingAnalysisStatus.Evaluated;

        var lineCount = source.Length == 0
            ? 0
            : source.Count(character => character == '\n') + 1;

        return ValueTask.FromResult(new NamingAnalysisResult(
            filePath, Language, framework, AnalyzerId, status,
            declarations, diagnostics, lineCount));
    }

    private sealed class DeclarationWalker : CSharpSyntaxWalker
    {
        private readonly string _filePath;
        private readonly string? _framework;
        private readonly List<NamingDeclaration> _declarations;
        private readonly CancellationToken _token;

        public DeclarationWalker(
            string filePath,
            string? framework,
            List<NamingDeclaration> declarations,
            CancellationToken token)
        {
            _filePath = filePath;
            _framework = framework;
            _declarations = declarations;
            _token = token;
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            Add(node.Identifier, NamingDeclarationKind.Type, node.Modifiers);
            base.VisitClassDeclaration(node);
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            Add(node.Identifier, NamingDeclarationKind.Interface, node.Modifiers);
            base.VisitInterfaceDeclaration(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            Add(node.Identifier, NamingDeclarationKind.Struct, node.Modifiers);
            base.VisitStructDeclaration(node);
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            Add(node.Identifier, NamingDeclarationKind.Record, node.Modifiers);
            base.VisitRecordDeclaration(node);
        }

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            Add(node.Identifier, NamingDeclarationKind.Enum, node.Modifiers);
            base.VisitEnumDeclaration(node);
        }

        public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            Add(node.Identifier, NamingDeclarationKind.Delegate, node.Modifiers);
            base.VisitDelegateDeclaration(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            Add(node.Identifier, NamingDeclarationKind.Method, node.Modifiers);
            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            Add(node.Identifier, NamingDeclarationKind.Constructor, node.Modifiers);
            base.VisitConstructorDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            Add(node.Identifier, NamingDeclarationKind.Property, node.Modifiers);
            base.VisitPropertyDeclaration(node);
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            var isConst = node.Modifiers.Any(SyntaxKind.ConstKeyword);
            foreach (var variable in node.Declaration.Variables)
                Add(variable.Identifier,
                    isConst ? NamingDeclarationKind.Constant : NamingDeclarationKind.Field,
                    node.Modifiers, isConst);
            base.VisitFieldDeclaration(node);
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            var isConst = node.Modifiers.Any(SyntaxKind.ConstKeyword);
            foreach (var variable in node.Declaration.Variables)
                Add(variable.Identifier,
                    isConst ? NamingDeclarationKind.Constant : NamingDeclarationKind.LocalVariable,
                    node.Modifiers, isConst);
            base.VisitLocalDeclarationStatement(node);
        }

        public override void VisitParameter(ParameterSyntax node)
        {
            Add(node.Identifier, NamingDeclarationKind.Parameter, node.Modifiers);
            base.VisitParameter(node);
        }

        private void Add(
            SyntaxToken identifier,
            NamingDeclarationKind kind,
            SyntaxTokenList modifiers,
            bool isConstant = false)
        {
            _token.ThrowIfCancellationRequested();
            if (identifier.IsMissing || string.IsNullOrWhiteSpace(identifier.ValueText))
                return;

            var position = identifier.GetLocation().GetLineSpan().StartLinePosition;
            // Explicit private/protected-private/private-protected declarations are
            // not public API. Internal and protected declarations are included in
            // the current public-facing naming policy; private is never evidence
            // of public visibility.
            var accessibility = modifiers.FirstOrDefault(modifier =>
                modifier.IsKind(SyntaxKind.PublicKeyword)
                || modifier.IsKind(SyntaxKind.PrivateKeyword)
                || modifier.IsKind(SyntaxKind.ProtectedKeyword)
                || modifier.IsKind(SyntaxKind.InternalKeyword));

            var isPublic =
                accessibility.IsKind(SyntaxKind.PublicKeyword)
                || accessibility.IsKind(SyntaxKind.ProtectedKeyword)
                || accessibility.IsKind(SyntaxKind.InternalKeyword);

            _declarations.Add(new NamingDeclaration(
                identifier.ValueText,
                kind,
                _filePath,
                position.Line + 1,
                position.Character + 1,
                "C#",
                _framework,
                Id,
                isPublic,
                modifiers.Any(SyntaxKind.StaticKeyword),
                isConstant));
        }
    }
}
