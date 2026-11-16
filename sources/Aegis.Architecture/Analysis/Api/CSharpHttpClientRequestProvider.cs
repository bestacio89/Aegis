using System.Security.Cryptography;
using System.Text;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aegis.Architecture.Analysis.Api;

/// <summary>
/// Discovers outbound requests made through explicitly declared System.Net.Http.HttpClient
/// instances. This is syntax-backed evidence, not a claim of runtime execution or reachability.
/// </summary>
public sealed class CSharpHttpClientRequestProvider
{
    private static readonly IReadOnlyDictionary<string, string> Methods =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GetAsync"] = "GET",
            ["GetStringAsync"] = "GET",
            ["GetByteArrayAsync"] = "GET",
            ["GetStreamAsync"] = "GET",
            ["PostAsync"] = "POST",
            ["PostAsJsonAsync"] = "POST",
            ["PutAsync"] = "PUT",
            ["PutAsJsonAsync"] = "PUT",
            ["PatchAsync"] = "PATCH",
            ["PatchAsJsonAsync"] = "PATCH",
            ["DeleteAsync"] = "DELETE"
        };

    public IReadOnlyList<ApiClientRequestEvidence> Discover(
        IEnumerable<string> sourceFiles,
        string modulePath)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);

        var module = Path.GetFullPath(modulePath);
        var evidence = new List<ApiClientRequestEvidence>();

        foreach (var sourceFile in sourceFiles
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Distinct(PathComparer)
                     .Where(File.Exists)
                     .OrderBy(path => path, PathComparer))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(sourceFile), path: sourceFile)
                .GetCompilationUnitRoot();
            var aliases = GetHttpClientAliases(root);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                    continue;

                var methodName = memberAccess.Name.Identifier.ValueText;
                var receiver = memberAccess.Expression;
                if (!IsHttpClientReceiver(receiver, invocation, root, aliases))
                    continue;

                string? httpMethod;
                ExpressionSyntax? uriExpression;
                var resolutionEvidence = new List<string>
                {
                    "Receiver is declared as System.Net.Http.HttpClient or constructed directly with HttpClient."
                };

                if (Methods.TryGetValue(methodName, out var knownMethod))
                {
                    httpMethod = knownMethod;
                    uriExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                    if (uriExpression is null)
                        resolutionEvidence.Add("Request URI argument is missing.");
                }
                else if (methodName == "SendAsync")
                {
                    var requestArgument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                    var request = ResolveHttpRequestMessage(requestArgument, root);
                    httpMethod = request.Method;
                    uriExpression = request.Uri;
                    resolutionEvidence.AddRange(request.Evidence);
                }
                else
                {
                    continue;
                }

                if (uriExpression is null)
                    continue;

                var (uri, status, uriEvidence) = ReadExpression(uriExpression);
                resolutionEvidence.AddRange(uriEvidence);

                var location = invocation.GetLocation().GetLineSpan();
                var caller = GetCallerIdentity(invocation);
                var identityInput = $"{sourceFile}|{invocation.SpanStart}|{invocation.Span.Length}|{methodName}";
                var evidenceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityInput)));

                evidence.Add(new ApiClientRequestEvidence
                {
                    EvidenceId = evidenceId,
                    Language = "C#",
                    Framework = "System.Net.Http.HttpClient",
                    ProviderId = "csharp-roslyn-httpclient-syntax",
                    ModulePath = module,
                    SourceFile = sourceFile,
                    StartLine = location.StartLinePosition.Line + 1,
                    StartColumn = location.StartLinePosition.Character + 1,
                    CallerIdentity = caller,
                    HttpMethod = httpMethod,
                    RequestUriExpression = uri,
                    ResolutionStatus = status,
                    ResolutionEvidence = Array.AsReadOnly(resolutionEvidence.ToArray()),
                    DeclarationEvidence = invocation.ToString()
                });
            }
        }

        return Array.AsReadOnly(evidence.ToArray());
    }

    private static IReadOnlySet<string> GetHttpClientAliases(CompilationUnitSyntax root)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directive in root.Usings)
        {
            if (directive.Alias is null && directive.Name?.ToString() == "System.Net.Http")
                aliases.Add("HttpClient");
            if (directive.Alias is not null
                && directive.Name?.ToString() == "System.Net.Http.HttpClient")
                aliases.Add(directive.Alias.Name.Identifier.ValueText);
        }

        return aliases;
    }

    private static bool IsHttpClientReceiver(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax invocation,
        CompilationUnitSyntax root,
        IReadOnlySet<string> aliases)
    {
        if (receiver is ParenthesizedExpressionSyntax parenthesized)
            return IsHttpClientReceiver(parenthesized.Expression, invocation, root, aliases);

        if (receiver is ObjectCreationExpressionSyntax creation)
            return IsHttpClientType(creation.Type.ToString(), aliases);

        if (receiver is ImplicitObjectCreationExpressionSyntax implicitCreation)
            return FindDeclaredType(implicitCreation, invocation, root, aliases);

        var name = receiver switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null
        };

        return name is not null && FindDeclaredType(receiver, invocation, root, aliases);
    }

    private static bool FindDeclaredType(
        SyntaxNode receiver,
        CompilationUnitSyntax root,
        IReadOnlySet<string> aliases)
    {
        var name = receiver switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null
        };
        if (name is null)
            return false;

        foreach (var declaration in root.DescendantNodes())
        {
            switch (declaration)
            {
                case VariableDeclaratorSyntax variable when variable.Identifier.ValueText == name
                    && variable.SpanStart < invocation.SpanStart
                    && variable.Ancestors().OfType<BlockSyntax>().FirstOrDefault() is { } localScope
                    && invocation.Ancestors().Contains(localScope):
                    if (variable.Parent is VariableDeclarationSyntax variables
                        && variables.Type is not null
                        && IsHttpClientType(variables.Type.ToString(), aliases))
                        return true;
                    if (variable.Initializer?.Value is ObjectCreationExpressionSyntax creation
                        && IsHttpClientType(creation.Type.ToString(), aliases))
                        return true;
                    if (variable.Initializer?.Value is ImplicitObjectCreationExpressionSyntax)
                        return false;
                    break;

                case ParameterSyntax parameter when parameter.Identifier.ValueText == name
                    && parameter.Ancestors().FirstOrDefault(ancestor => ancestor is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax) is { } parameterOwner
                    && invocation.Ancestors().Contains(parameterOwner):
                    if (IsHttpClientType(parameter.Type?.ToString(), aliases))
                        return true;
                    break;

                case PropertyDeclarationSyntax property when property.Identifier.ValueText == name
                    && property.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is { } propertyOwner
                    && invocation.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() == propertyOwner:
                    if (IsHttpClientType(property.Type.ToString(), aliases))
                        return true;
                    if (property.Initializer?.Value is ObjectCreationExpressionSyntax propertyCreation
                        && IsHttpClientType(propertyCreation.Type.ToString(), aliases))
                        return true;
                    break;

                case FieldDeclarationSyntax field when field.Declaration.Variables.Any(
                    variable => variable.Identifier.ValueText == name)
                    && field.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is { } fieldOwner
                    && invocation.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() == fieldOwner:
                    if (IsHttpClientType(field.Declaration.Type.ToString(), aliases))
                        return true;
                    break;
            }
        }

        return false;
    }

    private static bool IsHttpClientType(string? typeName, IReadOnlySet<string> aliases)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        var normalized = typeName.Replace("global::", string.Empty, StringComparison.Ordinal);
        var genericStart = normalized.IndexOf('<');
        if (genericStart >= 0)
            normalized = normalized[..genericStart];

        return aliases.Contains(normalized)
            || normalized == "System.Net.Http.HttpClient";
    }

    private static (string? Method, ExpressionSyntax? Uri, IReadOnlyList<string> Evidence)
        ResolveHttpRequestMessage(ExpressionSyntax? requestExpression, CompilationUnitSyntax root)
    {
        if (requestExpression is not IdentifierNameSyntax identifier)
            return (null, null, ["SendAsync request object could not be resolved to a local declaration."]);

        var declaration = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText);
        if (declaration?.Initializer?.Value is not ObjectCreationExpressionSyntax creation
            || !creation.Type.ToString().EndsWith("HttpRequestMessage", StringComparison.Ordinal))
            return (null, null, ["SendAsync request object is not a statically visible HttpRequestMessage construction."]);

        var arguments = creation.ArgumentList?.Arguments ?? default;
        if (arguments.Count < 2)
            return (null, null, ["HttpRequestMessage constructor does not expose both method and URI arguments."]);

        var method = arguments[0].Expression.ToString() switch
        {
            "HttpMethod.Get" => "GET",
            "HttpMethod.Post" => "POST",
            "HttpMethod.Put" => "PUT",
            "HttpMethod.Delete" => "DELETE",
            "HttpMethod.Patch" => "PATCH",
            "HttpMethod.Head" => "HEAD",
            "HttpMethod.Options" => "OPTIONS",
            _ => null
        };

        return (method, arguments[1].Expression,
            ["SendAsync was correlated to a local HttpRequestMessage constructor; custom HttpMethod expressions remain unknown."]);
    }

    private static (string Value, ApiClientRequestResolutionStatus Status, IReadOnlyList<string> Evidence)
        ReadExpression(ExpressionSyntax expression)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            return (expression.ToString(), ApiClientRequestResolutionStatus.Resolved, ["Request URI is a string literal."]);

        if (expression is InterpolatedStringExpressionSyntax)
            return (expression.ToString(), ApiClientRequestResolutionStatus.PartiallyResolved,
                ["Request URI contains interpolation; its original expression is retained."]);

        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
            return (expression.ToString(), ApiClientRequestResolutionStatus.PartiallyResolved,
                ["Request URI is composed from expressions; no constant evaluation was attempted."]);

        return (expression.ToString(), ApiClientRequestResolutionStatus.Dynamic,
            ["Request URI is not a string literal; the source expression is retained without evaluation."]);
    }

    private static string GetCallerIdentity(InvocationExpressionSyntax invocation)
    {
        var method = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is not null)
        {
            var type = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            return type is null
                ? method.Identifier.ValueText
                : $"{type.Identifier.ValueText}.{method.Identifier.ValueText}";
        }

        var localFunction = invocation.Ancestors().OfType<LocalFunctionStatementSyntax>().FirstOrDefault();
        return localFunction?.Identifier.ValueText ?? "<top-level>";
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
