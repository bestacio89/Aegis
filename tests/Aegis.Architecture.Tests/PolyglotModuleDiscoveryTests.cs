using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class PolyglotModuleDiscoveryTests
{
    [Fact]
    public void Detect_recognizes_Kotlin_Gradle_modules_without_misclassifying_Kotlin_DSL_as_source()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "src", "main", "kotlin"));
        File.WriteAllText(Path.Combine(directory.Path, "settings.gradle.kts"), "rootProject.name = \"kotlin-sample\"");
        File.WriteAllText(Path.Combine(directory.Path, "build.gradle.kts"), "plugins { kotlin(\"jvm\") version \"2.2.0\" }");
        File.WriteAllText(Path.Combine(directory.Path, "src", "main", "kotlin", "Application.kt"), "class Application");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);
        var module = Assert.Single(context.Modules);

        Assert.Equal("Kotlin", context.Language);
        Assert.Equal(ArchitectureBuildSystem.Gradle, context.BuildSystem);
        Assert.Equal(ArchitectureBuildSystem.Gradle, module.BuildSystem);
        Assert.Contains("Kotlin", module.Languages);
        Assert.Equal("KotlinProject", module.ProjectKind);
        Assert.Contains(ProjectModuleRole.Unknown, module.Roles);
    }

    [Fact]
    public void Detect_discovers_Python_pyproject_modules_and_Poetry_ownership()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "src", "sample"));
        File.WriteAllText(Path.Combine(directory.Path, "pyproject.toml"), """
            [project]
            name = "sample"
            version = "1.0.0"
            """);
        File.WriteAllText(Path.Combine(directory.Path, "poetry.lock"), "# lockfile");
        File.WriteAllText(Path.Combine(directory.Path, "src", "sample", "__init__.py"), "VALUE = 1");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);
        var module = Assert.Single(context.Modules);

        Assert.Equal("Python", context.Language);
        Assert.Equal(ArchitectureBuildSystem.Poetry, context.BuildSystem);
        Assert.Equal(ArchitectureBuildSystem.Poetry, module.BuildSystem);
        Assert.Contains("Python", module.Languages);
        Assert.Equal("PythonProject", module.ProjectKind);
    }

    [Fact]
    public void Detect_classifies_Node_backend_modules_from_runtime_framework_dependencies()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "src"));
        File.WriteAllText(Path.Combine(directory.Path, "package.json"), """
            {
              "name": "orders-api",
              "dependencies": {
                "express": "^5.0.0"
              },
              "devDependencies": {
                "typescript": "^5.9.0"
              }
            }
            """);
        File.WriteAllText(Path.Combine(directory.Path, "src", "server.mts"), "export const server = true;");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);
        var module = Assert.Single(context.Modules);

        Assert.Equal("TypeScript/JavaScript", context.Language);
        Assert.Equal(ArchitectureBuildSystem.Npm, module.BuildSystem);
        Assert.Contains("TypeScript", module.Languages);
        Assert.Contains("Express", module.Frameworks);
        Assert.Equal("BackendApplication", module.ProjectKind);
        Assert.True(module.IsWebProject);
        Assert.Contains(ProjectModuleRole.Backend, module.Roles);
        Assert.Contains(ProjectModuleRole.Executable, module.Roles);
    }

    [Fact]
    public void Detect_classifies_browser_frontend_modules_without_assigning_backend_role()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "src"));
        File.WriteAllText(Path.Combine(directory.Path, "package.json"), """
            {
              "name": "customer-portal",
              "dependencies": {
                "react": "^19.0.0"
              }
            }
            """);
        File.WriteAllText(Path.Combine(directory.Path, "src", "App.tsx"), "export const App = () => null;");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);
        var module = Assert.Single(context.Modules);

        Assert.Equal("FrontendApplication", module.ProjectKind);
        Assert.Contains(ProjectModuleRole.Frontend, module.Roles);
        Assert.DoesNotContain(ProjectModuleRole.Backend, module.Roles);
    }

    [Fact]
    public void Detect_classifies_mixed_Node_projects_as_full_stack()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "package.json"), """
            {
              "name": "full-stack",
              "dependencies": {
                "react": "^19.0.0",
                "express": "^5.0.0"
              }
            }
            """);
        File.WriteAllText(Path.Combine(directory.Path, "App.tsx"), "export const App = () => null;");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);
        var module = Assert.Single(context.Modules);

        Assert.Equal("FullStackApplication", module.ProjectKind);
        Assert.Contains(ProjectModuleRole.Frontend, module.Roles);
        Assert.Contains(ProjectModuleRole.Backend, module.Roles);
    }

    [Fact]
    public void Detect_classifies_shared_library_role_from_library_metadata_and_name()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Common.Contracts.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory.Path, "Contracts.cs"), "public interface IContract { }");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);
        var module = Assert.Single(context.Modules);

        Assert.True(module.IsLibrary);
        Assert.Contains(ProjectModuleRole.Library, module.Roles);
        Assert.Contains(ProjectModuleRole.Shared, module.Roles);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AegisPolyglot_{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
