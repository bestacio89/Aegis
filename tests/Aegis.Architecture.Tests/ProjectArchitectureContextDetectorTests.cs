using Aegis.Architecture.Analysis;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ProjectArchitectureContextDetectorTests
{
    [Fact]
    public void Detect_preserves_project_identity_and_initializes_detection_metadata()
    {
        using var directory = new TemporaryDirectory();

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal(Path.GetFileName(directory.Path), context.ProjectName);
        Assert.Equal(directory.Path, context.RootPath);
        Assert.Equal(0, context.FileCount);
        Assert.Equal("1.5.0", context.DetectorVersion);
        Assert.Equal("Hybrid", context.DetectionStrategy);
        Assert.Equal(context.ProjectName, context.MetadataMap["ProjectName"]);
        Assert.Equal("0", context.MetadataMap["FileCount"]);
        Assert.True(context.Confidence >= 0);
        Assert.True(context.Confidence <= 1);
    }

    [Fact]
    public void Detect_counts_project_files_but_excludes_generated_output()
    {
        using var directory = new TemporaryDirectory();

        var source = Path.Combine(directory.Path, "src", "Program.cs");
        var project = Path.Combine(directory.Path, "Sample.csproj");
        var generated = Path.Combine(directory.Path, "obj", "Generated.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(generated)!);
        File.WriteAllText(source, "public static class Program { }");
        File.WriteAllText(project, "<Project />");
        File.WriteAllText(generated, "generated");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal(2, context.FileCount);
        Assert.Equal("2", context.MetadataMap["FileCount"]);
        Assert.Equal(directory.Path, context.RootPath);
    }

    [Fact]
    public void Detect_handles_a_project_with_multiple_manifest_files()
    {
        using var directory = new TemporaryDirectory();

        File.WriteAllText(Path.Combine(directory.Path, "package.json"), """{"name":"fixture","dependencies":{}}""");
        File.WriteAllText(Path.Combine(directory.Path, "requirements.txt"), "pytest==8.0.0");
        File.WriteAllText(Path.Combine(directory.Path, "Dockerfile"), "FROM scratch");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal(3, context.FileCount);
        Assert.NotNull(context.MetadataMap);
        Assert.Equal("3", context.MetadataMap["FileCount"]);
        Assert.NotNull(context.DetectionStrategy);
    }


    [Fact]
    public void Detect_identifies_dotnet_manifests_entry_point_deployment_and_modules()
    {
        using var directory = new TemporaryDirectory();

        var program = Path.Combine(directory.Path, "Program.cs");
        var project = Path.Combine(directory.Path, "Sample.csproj");
        var dockerfile = Path.Combine(directory.Path, "Dockerfile");
        var deployment = Path.Combine(directory.Path, "k8s", "deployment.yaml");
        var moduleProject = Path.Combine(directory.Path, "src", "Application", "Application.csproj");
        var moduleSource = Path.Combine(directory.Path, "src", "Application", "OrderService.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(deployment)!);
        Directory.CreateDirectory(Path.GetDirectoryName(moduleProject)!);

        File.WriteAllText(program, "public static class Program { public static void Main() { } }");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
                <PackageReference Include="Franz.Common.DependencyInjection" Version="2.3.0" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(dockerfile, "FROM mcr.microsoft.com/dotnet/aspnet:10.0");
        File.WriteAllText(deployment, "apiVersion: apps/v1");
        File.WriteAllText(moduleProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(moduleSource, "public sealed class OrderService { }");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal("C#", context.Language);
        Assert.Equal(Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.DotNet, context.BuildSystem);
        Assert.Equal(".NET (EF Core)", context.Framework);
        Assert.Contains(context.DetectedDependencies, dependency =>
            dependency.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(context.Dependencies);
        Assert.Equal(program, context.EntryPointFile);
        Assert.True(context.IsDockerized);
        Assert.True(context.UsesKubernetes);
        Assert.True(context.IsDeployable);
        Assert.Equal(2, context.Modules.Count);
        Assert.Equal(2, context.Layers.Count);
        Assert.All(context.Modules, module =>
        {
            var layer = Assert.Single(module.Layers);
            Assert.Contains(context.Layers, candidate => ReferenceEquals(candidate, layer));
            Assert.NotEmpty(layer.Files);
        });
        Assert.Contains(context.PatternsDetected, pattern => pattern == "Repository Pattern");
        Assert.Contains(context.PatternsDetected, pattern => pattern == "Dependency Injection");
        Assert.Equal(context.Modules.Count.ToString(), context.MetadataMap["DetectedModules"]);
    }

    [Fact]
    public void Detect_maps_project_references_to_semantic_layer_edges()
    {
        using var directory = new TemporaryDirectory();

        var domainRoot = Path.Combine(directory.Path, "HeroService.Domain");
        var applicationRoot = Path.Combine(directory.Path, "HeroService.Application");
        Directory.CreateDirectory(domainRoot);
        Directory.CreateDirectory(applicationRoot);

        File.WriteAllText(
            Path.Combine(domainRoot, "HeroService.Domain.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(
            Path.Combine(domainRoot, "Order.cs"),
            "namespace HeroService.Domain; public sealed class Order { }");
        var applicationProject = Path.Combine(applicationRoot, "HeroService.Application.csproj");
        File.WriteAllText(
            applicationProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"..\\HeroService.Domain\\HeroService.Domain.csproj\" /></ItemGroup><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(
            Path.Combine(applicationRoot, "OrderHandler.cs"),
            "namespace HeroService.Application; public sealed class OrderHandler { }");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        var dependency = Assert.Single(context.Dependencies);
        Assert.Equal("HeroService.Application", dependency.Source);
        Assert.Equal("HeroService.Domain", dependency.Target);
        Assert.Equal("Application", dependency.SourceLayer);
        Assert.Equal("Domain", dependency.TargetLayer);
        Assert.Equal("ProjectReference", dependency.DependencyType);
        Assert.Equal(applicationProject, dependency.File);
    }

    [Fact]
    public void Detect_excludes_unity_cache_vendored_examples_and_generated_source()
    {
        using var directory = new TemporaryDirectory();

        var assets = Path.Combine(directory.Path, "Assets");
        Directory.CreateDirectory(Path.Combine(assets, "Scripts"));
        Directory.CreateDirectory(Path.Combine(assets, "TextMesh Pro", "Examples & Extras"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "Library", "PackageCache", "com.unity.sample"));

        File.WriteAllText(Path.Combine(assets, "Game.asmdef"), """{"name":"Game"}""");
        File.WriteAllText(Path.Combine(directory.Path, "Assembly-CSharp-Editor.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(directory.Path, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.0.0f1");
        File.WriteAllText(Path.Combine(assets, "Scripts", "GameController.cs"), "public sealed class GameController { }");
        File.WriteAllText(Path.Combine(assets, "Scripts", "Generated.Designer.cs"), "public sealed class Generated { }");
        File.WriteAllText(Path.Combine(assets, "TextMesh Pro", "Examples & Extras", "Example.cs"), "public sealed class Example { }");
        File.WriteAllText(Path.Combine(directory.Path, "Library", "PackageCache", "com.unity.sample", "Vendor.cs"), "public sealed class Vendor { }");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal(3, context.FileCount);
        Assert.Contains(context.Layers, layer => layer.Name == "Client");
        Assert.DoesNotContain(context.Layers, layer => layer.Files.Any(file =>
            file.Contains("PackageCache", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(context.Layers, layer => layer.Files.Any(file =>
            file.Contains("Examples & Extras", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Detect_classifies_server_hosted_blazor_as_full_stack_not_backend_only()
    {
        using var directory = new TemporaryDirectory();

        var project = Path.Combine(directory.Path, "BlazorApp.csproj");
        var component = Path.Combine(directory.Path, "Components", "Home.razor");
        Directory.CreateDirectory(Path.GetDirectoryName(component)!);

        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(component, """
            @page "/"
            <h1>Home</h1>
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal("C#", context.Language);
        Assert.Equal("ASP.NET Core", context.Framework);
        Assert.Contains("Blazor", context.DetectedFrameworks);
        Assert.Contains("ASP.NET Core", context.DetectedFrameworks);
        Assert.Equal("FullStack", context.DomainType);
    }

 
    [Fact]
    public void Detect_preserves_blazor_backend_and_unity_signals_in_a_mixed_repository()
    {
        using var directory = new TemporaryDirectory();

        var webProject = Path.Combine(directory.Path, "Server", "Server.csproj");
        var component = Path.Combine(directory.Path, "Server", "Components", "Home.razor");
        var unityAssembly = Path.Combine(directory.Path, "UnityClient", "Assets", "Game", "Game.asmdef");
        var unityVersion = Path.Combine(directory.Path, "UnityClient", "ProjectSettings", "ProjectVersion.txt");
        var unityManifest = Path.Combine(directory.Path, "UnityClient", "Packages", "manifest.json");

        Directory.CreateDirectory(Path.GetDirectoryName(webProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(component)!);
        Directory.CreateDirectory(Path.GetDirectoryName(unityAssembly)!);
        Directory.CreateDirectory(Path.GetDirectoryName(unityVersion)!);
        Directory.CreateDirectory(Path.GetDirectoryName(unityManifest)!);

        File.WriteAllText(webProject, """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(component, "@page \"/\"\n<h1>Home</h1>");
        File.WriteAllText(unityAssembly, "{ \"name\": \"Game\" }");
        File.WriteAllText(unityVersion, "m_EditorVersion: 6000.0.0f1");
        File.WriteAllText(unityManifest, "{ \"dependencies\": { \"com.unity.inputsystem\": \"1.0.0\" } }");
        File.WriteAllText(Path.Combine(directory.Path, "Server", "Program.cs"), "public class Program { }");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal("ASP.NET Core", context.Framework);
        Assert.Contains("ASP.NET Core", context.DetectedFrameworks);
        Assert.Contains("Blazor", context.DetectedFrameworks);
        Assert.Contains("Unity", context.DetectedFrameworks);
        Assert.Equal("FullStack", context.DomainType);
        Assert.Contains(context.Modules, module =>
            module.Type == "Unity"
            && string.Equals(module.Path, Path.Combine(directory.Path, "UnityClient"), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AegisContextTests_{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
