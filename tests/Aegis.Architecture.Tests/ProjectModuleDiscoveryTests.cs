using Aegis.Architecture.Analysis;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ProjectModuleDiscoveryTests
{
    [Fact]
    public void Detect_uses_package_manifest_name_instead_of_generic_package_filename()
    {
        using var project = new TemporaryProject();
        project.Write("client-app/package.json", """
            {
              "name": "@sample/client-app",
              "private": true,
              "scripts": { "build": "tsc" }
            }
            """);
        project.Write("client-app/src/index.ts", "export const ready = true;");

        var context = ProjectArchitectureContextDetector.Detect(project.Root);

        var module = Assert.Single(context.Modules, item =>
            item.Path.EndsWith("client-app", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("@sample/client-app", module.Name);
        Assert.NotEqual("package", module.Name, ignoreCase: true);
        Assert.Contains(context.Layers, layer =>
            layer.Name.Equals("Client", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_uses_maven_artifact_id_instead_of_generic_pom_filename()
    {
        using var project = new TemporaryProject();
        project.Write("sample-api/pom.xml", """
            <project xmlns="http://maven.apache.org/POM/4.0.0">
              <modelVersion>4.0.0</modelVersion>
              <groupId>org.sample</groupId>
              <artifactId>sample-api</artifactId>
              <version>1.0.0</version>
            </project>
            """);
        project.Write("sample-api/src/main/java/org/sample/App.java", "class App {}");

        var context = ProjectArchitectureContextDetector.Detect(project.Root);

        var module = Assert.Single(context.Modules, item =>
            item.Path.EndsWith("sample-api", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("sample-api", module.Name);
        Assert.NotEqual("pom", module.Name, ignoreCase: true);
        Assert.Contains(context.Layers, layer =>
            layer.Name.Equals("Api", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TemporaryProject : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"AegisModuleDiscovery_{Guid.NewGuid():N}");

        public TemporaryProject() => Directory.CreateDirectory(Root);

        public void Write(string relativePath, string content)
        {
            var fullPath = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
