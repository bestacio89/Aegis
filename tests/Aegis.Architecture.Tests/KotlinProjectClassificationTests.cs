using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Enums;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinProjectClassificationTests
{
    [Fact]
    public void Detect_classifies_polyglot_root_as_multi_language_and_maps_kotlin_modules_to_architecture_layers()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aegis-kotlin-classification-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(
                Path.Combine(root, "Host.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Host.cs"), "public sealed class Host { }");

            var expectedLayers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["app"] = "Application",
                ["backend"] = "Api",
                ["common"] = "Shared",
                ["db"] = "Infrastructure",
                ["wearApp"] = "Client",
                ["ios-export"] = "Client",
                ["compose-web"] = "Presentation",
                ["compose-desktop"] = "Presentation",
                ["mcp-server"] = "Api"
            };

            foreach (var module in expectedLayers.Keys)
            {
                var moduleRoot = Path.Combine(root, module);
                var sourceDirectory = Path.Combine(moduleRoot, "src", "main", "kotlin", "sample", module.Replace('-', '_'));
                Directory.CreateDirectory(sourceDirectory);
                File.WriteAllText(
                    Path.Combine(moduleRoot, "build.gradle.kts"),
                    "plugins { kotlin(\"jvm\") version \"2.0.0\" }");
                File.WriteAllText(
                    Path.Combine(sourceDirectory, "Module.kt"),
                    $"package sample.{module.Replace('-', '_')}\nclass Module{module.Replace("-", string.Empty)}");
            }

            var context = ProjectArchitectureContextDetector.Detect(root);

            Assert.Equal("Multi-Language", context.Language);
            Assert.Equal(ArchitectureBuildSystem.Unknown, context.BuildSystem);

            foreach (var (moduleName, expectedLayer) in expectedLayers)
            {
                var module = Assert.Single(context.Modules, candidate =>
                    candidate.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(expectedLayer, Assert.Single(module.Layers).Name);
            }

            Assert.DoesNotContain(context.Modules, module =>
                module.Layers.Any(layer => layer.Name.StartsWith("Unclassified", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
