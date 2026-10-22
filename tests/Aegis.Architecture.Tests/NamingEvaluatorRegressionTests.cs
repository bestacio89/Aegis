using Aegis.Architecture.Bootstrap;
using Aegis.Architecture.Evaluators.Naming;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class NamingEvaluatorRegressionTests
{
    [Fact]
    public async Task Blazor_code_behind_uses_component_stem_and_excludes_generated_or_vendored_files()
    {
        using var project = new TemporaryProject();
        var componentPath = project.Write(
            "Components/CreateSkill.razor.cs",
            "namespace Fixture; public partial class CreateSkill { public void Save() { } }");
        project.Write(
            "Components/Generated.Designer.cs",
            "public sealed class Generated { public void Bad_name() { } }");
        project.Write(
            "Assets/TextMesh Pro/Examples & Extras/Example.cs",
            "public sealed class Example { public void Bad_name() { } }");
        var unityPath = project.Write(
            "sources/client/Assets/Scripts/UnityFieldFixture.cs",
            "public sealed class UnityFieldFixture { private int m_Text; public void Start() { } }");
        project.Write("sources/client/Assets/Game.asmdef", """{"name":"Game"}""");
        project.Write("sources/client/ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.0.0f1");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAegisCore();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var evaluator = Assert.Single(
            scope.ServiceProvider.GetServices<IEvaluator>(),
            item => item.Name == "NamingEvaluator");

        var context = new ProjectArchitectureContext
        {
            ProjectName = "BlazorNamingFixture",
            RootPath = project.Root,
            Language = "C#",
            Framework = "Blazor",
            Confidence = 0.95
        };

        var results = (await evaluator.EvaluateAsync(project.Root, context)).ToArray();
        var component = Assert.Single(
            results,
            result => string.Equals(
                result.Metadata.GetValueOrDefault("FilePath"),
                componentPath,
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(100, component.Metrics["FileNamingCompliance"]);
        Assert.Equal("Blazor", component.Metadata["Framework"]);

        var unity = Assert.Single(
            results,
            result => string.Equals(
                result.Metadata.GetValueOrDefault("FilePath"),
                unityPath,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Unity", unity.Metadata["Framework"]);
        Assert.Equal(100, unity.Metrics["VariableNamingCompliance"]);

        Assert.DoesNotContain(results, result =>
            result.Metadata.GetValueOrDefault("FilePath")?.EndsWith(
                "Generated.Designer.cs",
                StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(results, result =>
            result.Metadata.GetValueOrDefault("FilePath")?.Contains(
                "Examples & Extras",
                StringComparison.OrdinalIgnoreCase) == true);
    }

    private sealed class TemporaryProject : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"AegisNamingRegression_{Guid.NewGuid():N}");

        public TemporaryProject() => Directory.CreateDirectory(Root);

        public string Write(string relativePath, string content)
        {
            var fullPath = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
