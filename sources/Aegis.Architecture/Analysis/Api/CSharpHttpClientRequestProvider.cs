using System.Security.Cryptography;
using System.Text;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aegis.Architecture.Analysis.Api;

/// <summary>
/// Discovers outbound requests whose receiver and invoked API resolve to the
/// framework's HttpClient symbols. Source syntax is retained as evidence.
/// </summary>
public sealed class CSharpHttpClientRequestProvider
{
    private const string HttpClientType = "System.Net.Http.HttpClient";
    private const string HttpClientJsonExtensionsType = "System.Net.Http.Json.HttpClientJsonExtensions";

    private static readonly IReadOnlyDictionary<string, string> HttpMethods =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GetAsync"] = "GET",
            ["GetStringAsync"] = "GET",
            ["GetByteArrayAsync"] = "GET",
            ["GetStreamAsync"] = "GET",
            ["GetFromJsonAsync"] = "GET",
            ["PostAsync"] = "POST",
            ["PostAsJsonAsync"] = "POST",
            ["PutAsync"] = "PUT",
            ["PutAsJsonAsync"] = "PUT",
            ["PatchAsync"] = "PATCH",
            ["PatchAsJsonAsync"] = "PATCH",
            ["DeleteAsync"] = "DELETE"
        };

    private static readonly Lazy<IReadOnlyList<MetadataReference>> PlatformReferences =
        new(CreatePlatformReferences);

    public IReadOnlyList<ApiClientRequestEvidence> Discover(
        IEnumerable<string> sourceFiles,
        string modulePath)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);

        var module = Path.GetFullPath(modulePath);
        var files = sourceFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .Where(File.Exists)
            .OrderBy(path => path, PathComparer)
            .ToArray();

        if (files.Length == 0 || PlatformReferences.Value.Count == 0)
            return Array.Empty<ApiClientRequestEvidence>();

        var trees = files.Select(file =>
            CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file)).ToArray();
        var compilation = CSharpCompilation.Create(
            "Aegis.ApiClientDiscovery",
            trees,
            PlatformReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var evidence = new List<ApiClientRequestEvidence>();
        foreach (var tree in trees)
        {
            var root = tree.GetCompilationUnitRoot();
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                    continue;

                var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (method is null || !TryGetHttpMethod(method, out var httpMethod))
                    continue;

                var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
                if (receiverType?.ToDisplayString() != HttpClientType)
                    continue;

                ExpressionSyntax? uriExpression;
                var resolutionEvidence = new List<string>
                {
                    "Roslyn resolved the invocation and receiver to the System.Net.Http HttpClient API."
                };

                if (method.Name == "SendAsync")
                {
                    var requestExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                    var request = ResolveHttpRequestMessage(requestExpression, semanticModel);
                    httpMethod = request.Method;
                    uriExpression = request.Uri;
                    resolutionEvidence.AddRange(request.Evidence);
                }
                else
                {
                    uriExpression = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                }

                if (uriExpression is null)
                {
                    resolutionEvidence.Add("Request URI could not be recovered from the call site.");
                    continue;
                }

                var (uri, status, uriEvidence) = ReadExpression(uriExpression);
                resolutionEvidence.AddRange(uriEvidence);

                var sourceFile = tree.FilePath;
                var location = invocation.GetLocation().GetLineSpan();
                var caller = GetCallerIdentity(invocation);
                var identityInput = $"{sourceFile}|{invocation.SpanStart}|{invocation.Span.Length}|{method.Name}";
                var evidenceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityInput)));

                evidence.Add(new ApiClientRequestEvidence
                {
                    EvidenceId = evidenceId,
                    Language = "C#",
                    Framework = "System.Net.Http.HttpClient",
                    ProviderId = "csharp-roslyn-httpclient-symbols",
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

    private static IReadOnlyList<MetadataReference> CreatePlatformReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
            return Array.Empty<MetadataReference>();

        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(PathComparer)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static bool TryGetHttpMethod(IMethodSymbol method, out string? httpMethod)
    {
        httpMethod = null;
        var declaringType = method.ContainingType.ToDisplayString();

        if (declaringType == HttpClientType && method.Name == "SendAsync")
            return true;

        if (declaringType == HttpClientType && HttpMethods.TryGetValue(method.Name, out httpMethod))
            return true;

        if (declaringType == HttpClientJsonExtensionsType
            && HttpMethods.TryGetValue(method.Name, out httpMethod))
            return true;

        return false;
    }

    private static (string? Method, ExpressionSyntax? Uri, IReadOnlyList<string> Evidence)
        ResolveHttpRequestMessage(
            ExpressionSyntax? requestExpression,
            SemanticModel semanticModel)
    {
        if (requestExpression is null)
            return (null, null, ["SendAsync request argument is missing."]);

        var symbol = semanticModel.GetSymbolInfo(requestExpression).Symbol;
        var declaration = symbol?.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();

        if (declaration?.Initializer?.Value is not ObjectCreationExpressionSyntax creation
            || creation.Type.ToString().EndsWith("HttpRequestMessage", StringComparison.Ordinal) is false)
            return (null, null, ["SendAsync request object is not a statically visible HttpRequestMessage construction."]);

        var arguments = creation.ArgumentList?.Arguments ?? default;
        if (arguments.Count < 2)
            return (null, null, ["HttpRequestMessage constructor does not expose both method and URI arguments."]);

        var method = semanticModel.GetConstantValue(arguments[0].Expression);
        var methodName = arguments[0].Expression.ToString();
        var httpMethod = methodName switch
        {
            "HttpMethod.Get" => "GET",
            "HttpMethod.Post" => "POST",
            "HttpMethod.Put" => "PUT",
            "HttpMethod.Delete" => "DELETE",
            "HttpMethod.Patch" => "PATCH",
            "HttpMethod.Head" => "HEAD",
            "HttpMethod.Options" => "OPTIONS",
            _ => method.HasValue && method.Value is string value
                ? value.ToUpperInvariant()
                : null
        };

        return (httpMethod, arguments[1].Expression,
            ["SendAsync was correlated to the local HttpRequestMessage declaration; dynamic HttpMethod expressions remain unknown."]);
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
