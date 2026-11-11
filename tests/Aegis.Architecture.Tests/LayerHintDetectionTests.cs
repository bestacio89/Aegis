using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Models.Policies.Architecture;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class LayerHintDetectionTests
{
    [Fact]
    public void Context_detection_applies_explicit_project_name_layer_hints()
    {
        var root = CreateProject("Kernel");

        try
        {
            var policy = new ArchitecturePolicy
            {
                LayerHints = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Domain"] = ["Kernel"]
                }
            };

            var context = ProjectArchitectureContextDetector.Detect(root, policy);

            var module = Assert.Single(context.Modules);
            var layer = Assert.Single(module.Layers);

            Assert.Equal("Domain", layer.Name);
            Assert.Contains(context.Layers, item => item.Name == "Domain");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Context_detection_keeps_modules_unclassified_when_layer_hints_are_ambiguous()
    {
        var root = CreateProject("Kernel");

        try
        {
            var policy = new ArchitecturePolicy
            {
                LayerHints = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Domain"] = ["Kernel"],
                    ["Infrastructure"] = ["Kernel"]
                }
            };

            var context = ProjectArchitectureContextDetector.Detect(root, policy);

            var module = Assert.Single(context.Modules);
            var layer = Assert.Single(module.Layers);

            Assert.Equal("Unclassified:Kernel", layer.Name);
            Assert.DoesNotContain(
                context.Layers,
                item => item.Name.Equals("Domain", StringComparison.OrdinalIgnoreCase)
                    || item.Name.Equals("Infrastructure", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateProject(string projectName)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Aegis.LayerHints",
            Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "src", projectName);

        Directory.CreateDirectory(projectDirectory);

        File.WriteAllText(
            Path.Combine(projectDirectory, $"{projectName}.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(projectDirectory, $"{projectName}.cs"),
            "namespace Aegis.LayerHints; public sealed class Kernel { }");

        return root;
    }
}
