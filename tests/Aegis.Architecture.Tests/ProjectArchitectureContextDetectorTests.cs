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
    public void Detect_reports_an_unavailable_root_as_incomplete_discovery()
    {
        var missingRoot = Path.Combine(
            Path.GetTempPath(),
            $"AegisMissingRoot_{Guid.NewGuid():N}");

        var context = ProjectArchitectureContextDetector.Detect(missingRoot);

        var diagnostic = Assert.Single(context.DiscoveryDiagnostics);
        Assert.Equal("DirectoryDiscoveryFailed", diagnostic.Code);
        Assert.Equal("ProjectArchitectureContextDetector", diagnostic.Component);
        Assert.Equal(missingRoot, diagnostic.Target);
        Assert.True(diagnostic.AffectsCoverage);
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
    public void Detect_reports_excluded_path_entries_by_reason()
    {
        using var directory = new TemporaryDirectory();

        var sourceDirectory = Path.Combine(directory.Path, "src");
        var generatedDirectory = Path.Combine(directory.Path, "Generated");
        var thirdPartyDirectory = Path.Combine(directory.Path, "ThirdParty");
        var textMeshProExamples = Path.Combine(
            directory.Path,
            "TextMesh Pro",
            "Examples & Extras");

        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(generatedDirectory);
        Directory.CreateDirectory(thirdPartyDirectory);
        Directory.CreateDirectory(textMeshProExamples);

        File.WriteAllText(Path.Combine(sourceDirectory, "Widget.cs"), "public class Widget { }");
        File.WriteAllText(Path.Combine(sourceDirectory, "Generated.Designer.cs"), "public class Generated { }");
        File.WriteAllText(Path.Combine(generatedDirectory, "Output.cs"), "generated");
        File.WriteAllText(Path.Combine(thirdPartyDirectory, "Vendor.cs"), "vendor");
        File.WriteAllText(Path.Combine(textMeshProExamples, "TMP_DigitValidator.cs"), "vendor example");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal(1, context.FileCount);
        Assert.Equal(4, context.ExcludedEntriesByReason.Values.Sum());
        Assert.Equal(1, context.ExcludedEntriesByReason["GeneratedCode"]);
        Assert.Equal(1, context.ExcludedEntriesByReason["GeneratedFile"]);
        Assert.Equal(1, context.ExcludedEntriesByReason["VendoredCode"]);
        Assert.Equal(1, context.ExcludedEntriesByReason["VendoredExample"]);
        Assert.Equal("4", context.MetadataMap["ExcludedPathEntryCount"]);
        Assert.Contains("VendoredExample", context.MetadataMap["ExcludedEntriesByReason"], StringComparison.Ordinal);
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
        Assert.Equal(".NET", context.Framework);
        Assert.Contains(context.Modules, module =>
            module.ProjectFile == project
            && module.Framework == ".NET (EF Core)");
        Assert.Contains(context.DetectedDependencies, dependency =>
            dependency.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(context.Dependencies);
        Assert.Equal(program, context.EntryPointFile);
        Assert.True(context.IsDockerized);
        Assert.True(context.UsesKubernetes);
        Assert.True(context.IsDeployable);
        Assert.Equal(2, context.Modules.Count);
        Assert.Equal(2, context.Layers.Count);
        Assert.Equal(
            context.Layers.Count,
            context.Layers.Select(layer => layer.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(context.Modules, module =>
        {
            var layer = Assert.Single(module.Layers);
            Assert.Contains(context.Layers, candidate =>
                string.Equals(candidate.Name, layer.Name, StringComparison.OrdinalIgnoreCase)
                && candidate.Files.Any(file => layer.Files.Contains(file, StringComparer.OrdinalIgnoreCase)));
            Assert.NotEmpty(layer.Files);
        });
        Assert.Contains(context.PatternsDetected, pattern => pattern == "Repository Pattern");
        Assert.Contains(context.PatternsDetected, pattern => pattern == "Dependency Injection");
        Assert.Equal(context.Modules.Count.ToString(), context.MetadataMap["DetectedModules"]);
    }

    [Fact]
    public void Detect_resolves_nested_source_files_to_their_nearest_project_manifest()
    {
        using var directory = new TemporaryDirectory();

        var libraryProject = Path.Combine(directory.Path, "Library", "Library.csproj");
        var testProject = Path.Combine(directory.Path, "Library.Tests", "Library.Tests.csproj");
        var nestedSource = Path.Combine(directory.Path, "Library", "Handlers", "OrderHandler.cs");
        var testSource = Path.Combine(directory.Path, "Library.Tests", "Handlers", "OrderHandlerTests.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(libraryProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(testProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(nestedSource)!);
        Directory.CreateDirectory(Path.GetDirectoryName(testSource)!);

        File.WriteAllText(libraryProject, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(testProject, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.0.0" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(nestedSource, "public sealed class OrderHandler { }");
        File.WriteAllText(testSource, "public sealed class OrderHandlerTests { }");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        var library = Assert.Single(context.Modules, module => module.Name == "Library");
        var tests = Assert.Single(context.Modules, module => module.Name == "Library.Tests");

        Assert.Equal("ClassLibrary", library.ProjectKind);
        Assert.False(library.IsTestProject);
        Assert.Contains("C#", library.Languages);
        Assert.Contains(nestedSource, library.Layers.Single().Files);

        Assert.Equal("Test", tests.ProjectKind);
        Assert.True(tests.IsTestProject);
        Assert.Equal("Test", tests.Type);
        Assert.Contains(testSource, tests.Layers.Single().Files);
        Assert.DoesNotContain(testSource, library.Layers.Single().Files);
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
        Directory.CreateDirectory(Path.Combine(assets, "Examples & Extras"));
        Directory.CreateDirectory(Path.Combine(assets, "TextMeshPro", "Custom"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "Library", "PackageCache", "com.unity.sample"));

        File.WriteAllText(Path.Combine(assets, "Game.asmdef"), """{"name":"Game"}""");
        File.WriteAllText(Path.Combine(directory.Path, "Assembly-CSharp-Editor.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(directory.Path, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.0.0f1");
        File.WriteAllText(Path.Combine(assets, "Scripts", "GameController.cs"), "public sealed class GameController { }");
        File.WriteAllText(Path.Combine(assets, "Scripts", "Generated.Designer.cs"), "public sealed class Generated { }");
        File.WriteAllText(Path.Combine(assets, "TextMesh Pro", "Examples & Extras", "Example.cs"), "public sealed class Example { }");
        File.WriteAllText(Path.Combine(assets, "Examples & Extras", "GameOwnedExample.cs"), "public sealed class GameOwnedExample { }");
        File.WriteAllText(Path.Combine(assets, "TextMeshPro", "Custom", "HudLabel.cs"), "public sealed class HudLabel { }");
        File.WriteAllText(Path.Combine(directory.Path, "Library", "PackageCache", "com.unity.sample", "Vendor.cs"), "public sealed class Vendor { }");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal(5, context.FileCount);
        Assert.Contains(context.Layers, layer => layer.Name == "Client");
        Assert.DoesNotContain(context.Layers, layer => layer.Files.Any(file =>
            file.Contains("PackageCache", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(context.Layers, layer => layer.Files.Any(file =>
            file.Contains(Path.Combine("TextMesh Pro", "Examples & Extras"), StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(context.Layers, layer => layer.Files.Any(file =>
            file.Contains("GameOwnedExample.cs", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(context.Layers, layer => layer.Files.Any(file =>
            file.Contains("HudLabel.cs", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Detect_distinguishes_test_projects_from_testing_support_libraries()
    {
        using var directory = new TemporaryDirectory();

        var testingLibrary = Path.Combine(directory.Path, "sources", "Franz.Common.Testing");
        var testProject = Path.Combine(directory.Path, "tests", "Franz.Common.Tests");
        Directory.CreateDirectory(testingLibrary);
        Directory.CreateDirectory(testProject);

        File.WriteAllText(
            Path.Combine(testingLibrary, "Franz.Common.Testing.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(testingLibrary, "UnitTest.cs"),
            "public abstract class UnitTest { }");
        File.WriteAllText(
            Path.Combine(testProject, "Franz.Common.Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(testProject, "EntityTests.cs"),
            "public sealed class EntityTests { }");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);
        var supportModule = Assert.Single(
            context.Modules,
            module => module.Name == "Franz.Common.Testing");
        var testsModule = Assert.Single(
            context.Modules,
            module => module.Name == "Franz.Common.Tests");

        Assert.False(supportModule.IsTestProject);
        Assert.True(supportModule.IsTestSupport);
        Assert.Equal("TestSupport", supportModule.ProjectKind);
        Assert.Equal("Tests", Assert.Single(supportModule.Layers).Name);
        Assert.True(testsModule.IsTestProject);
        Assert.Equal("Test", testsModule.ProjectKind);
        Assert.Equal("Test", testsModule.Type);
        Assert.Equal("Library", context.DomainType);
        Assert.Empty(context.ProductionModules);
        Assert.Single(context.TestSupportModules);
        Assert.Single(context.TestModules);
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
        var module = Assert.Single(context.Modules);
        Assert.Equal("Multi-Framework", module.Framework);
        Assert.Contains("Blazor", module.Frameworks);
        Assert.Contains("ASP.NET Core", module.Frameworks);
        Assert.Equal("FullStack", context.DomainType);
    }

 
    [Fact]
    public void Detect_excludes_test_projects_from_repository_primary_framework_aggregation()
    {
        using var directory = new TemporaryDirectory();

        var webRoot = Path.Combine(directory.Path, "src", "Web");
        var testsRoot = Path.Combine(directory.Path, "tests", "Web.Tests");
        Directory.CreateDirectory(webRoot);
        Directory.CreateDirectory(testsRoot);

        File.WriteAllText(
            Path.Combine(webRoot, "Web.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(testsRoot, "Web.Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.0.0" />
              </ItemGroup>
            </Project>
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal("ASP.NET Core", context.Framework);
        Assert.Contains(context.Modules, module =>
            module.Name == "Web.Tests" && module.IsTestProject);
    }

    [Fact]
    public void Detect_discovers_frontend_manifests_without_overwriting_backend_identity()
    {
        using var directory = new TemporaryDirectory();

        var serverRoot = Path.Combine(directory.Path, "Server");
        var clientRoot = Path.Combine(directory.Path, "Client");
        var adminRoot = Path.Combine(clientRoot, "Admin");
        Directory.CreateDirectory(serverRoot);
        Directory.CreateDirectory(adminRoot);

        File.WriteAllText(
            Path.Combine(serverRoot, "Server.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(clientRoot, "package.json"),
            """
            {
              "name": "client",
              "dependencies": {
                "@angular/core": "^20.0.0",
                "rxjs": "^7.0.0"
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(adminRoot, "package.json"),
            """
            {
              "name": "admin",
              "devDependencies": {
                "@angular/cli": "^20.0.0",
                "typescript": "~5.0.0"
              },
              "peerDependencies": {
                "shared-ui": "^1.0.0"
              }
            }
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal("C#", context.Language);
        Assert.Equal("Multi-Framework", context.Framework);
        Assert.Contains("ASP.NET Core", context.DetectedFrameworks);
        Assert.Contains("Angular", context.DetectedFrameworks);
        Assert.Equal("FullStack", context.DomainType);
        Assert.Equal("MultiProject", context.Nature);
        Assert.Contains(context.Modules, module =>
            module.Name == "Server" && module.Framework == "ASP.NET Core");
        Assert.Contains(context.Modules, module =>
            module.Name == "client" && module.Framework == "Angular");
        Assert.Contains(context.Modules, module =>
            module.Name == "admin" && module.Framework == "Angular");
        Assert.Contains(context.DetectedDependencies, dependency =>
            dependency.Equals("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.DetectedDependencies, dependency =>
            dependency.Equals("@angular/core", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.DetectedDependencies, dependency =>
            dependency.Equals("rxjs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.DetectedDependencies, dependency =>
            dependency.Equals("@angular/cli", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.DetectedDependencies, dependency =>
            dependency.Equals("typescript", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.DetectedDependencies, dependency =>
            dependency.Equals("shared-ui", StringComparison.OrdinalIgnoreCase));
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

        Assert.Equal("Multi-Framework", context.Framework);
        Assert.Contains("ASP.NET Core", context.DetectedFrameworks);
        Assert.Contains("Blazor", context.DetectedFrameworks);
        Assert.Contains("Unity", context.DetectedFrameworks);
        Assert.Equal("Mixed", context.DomainType);
        Assert.Equal("MultiProject", context.Nature);
        Assert.Contains(context.Modules, module =>
            module.Type == "Unity"
            && string.Equals(module.Path, Path.Combine(directory.Path, "UnityClient"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_classifies_foundational_runtime_and_abstraction_modules_as_infrastructure()
    {
        using var directory = new TemporaryDirectory();

        var moduleNames = new[]
        {
            "Mediator",
            "Messaging",
            "Http",
            "Http.Client",
            "Http.Authentication",
            "Serialization",
            "Reflection",
            "DependencyInjection",
            "Bootstrap",
            "Hosting",
            "Base.Abstractions"
        };

        foreach (var moduleName in moduleNames)
        {
            var modulePath = Path.Combine(directory.Path, "src", moduleName);
            Directory.CreateDirectory(modulePath);

            File.WriteAllText(
                Path.Combine(modulePath, $"{moduleName}.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(modulePath, "Component.cs"),
                "public sealed class Component { }");
        }

        var contractPath = Path.Combine(directory.Path, "src", "Http.Contracts");
        Directory.CreateDirectory(contractPath);
        File.WriteAllText(
            Path.Combine(contractPath, "Http.Contracts.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal(moduleNames.Length + 1, context.Modules.Count);
        foreach (var moduleName in moduleNames)
        {
            var module = Assert.Single(context.Modules, item => item.Name == moduleName);
            Assert.Equal("Infrastructure", module.Type);
            Assert.Equal("Infrastructure", Assert.Single(module.Layers).Name);
        }

        var contractsModule = Assert.Single(
            context.Modules,
            module => module.Name == "Http.Contracts");
        Assert.Equal("Contracts", contractsModule.Type);
        Assert.Equal("Contracts", Assert.Single(contractsModule.Layers).Name);
    }

    [Fact]
    public void Detect_classifies_business_and_transport_neutral_contract_package_families()
    {
        using var directory = new TemporaryDirectory();

        var expectedLayers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Business"] = "Domain",
            ["Headers"] = "Contracts",
            ["Errors"] = "Contracts",
            ["Annotations"] = "Contracts"
        };

        foreach (var moduleName in expectedLayers.Keys)
        {
            var modulePath = Path.Combine(directory.Path, "sources", $"Franz.Common.{moduleName}");
            Directory.CreateDirectory(modulePath);
            File.WriteAllText(
                Path.Combine(modulePath, $"Franz.Common.{moduleName}.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                </Project>
                """);
        }

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        foreach (var expected in expectedLayers)
        {
            var module = Assert.Single(
                context.Modules,
                candidate => candidate.Name == $"Franz.Common.{expected.Key}");
            Assert.Equal(expected.Value, module.Type);
            Assert.Equal(expected.Value, Assert.Single(module.Layers).Name);
        }
    }

    [Fact]
    public void Detect_classifies_projects_under_test_directories_as_test_scope_without_name_or_sdk_hints()
    {
        using var directory = new TemporaryDirectory();

        var testProjectPath = Path.Combine(directory.Path, "tests", "IntegrationHarness");
        Directory.CreateDirectory(testProjectPath);
        File.WriteAllText(
            Path.Combine(testProjectPath, "IntegrationHarness.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);
        var module = Assert.Single(context.Modules);

        Assert.True(module.IsTestProject);
        Assert.Equal("Test", module.ProjectKind);
        Assert.Equal("Test", module.Type);
        Assert.Equal("Tests", Assert.Single(module.Layers).Name);
        Assert.Empty(context.Layers);
        Assert.Empty(context.ProductionModules);
        Assert.Single(context.TestModules);
    }

    [Fact]
    public void Detect_preserves_test_project_relationships_without_mixing_them_into_production_architecture()
    {
        using var directory = new TemporaryDirectory();

        var httpRoot = Path.Combine(directory.Path, "sources", "Franz.Common.Http");
        var serializationRoot = Path.Combine(directory.Path, "sources", "Franz.Common.Serialization");
        var testsRoot = Path.Combine(directory.Path, "tests", "Franz.Common.Http.Tests");
        Directory.CreateDirectory(httpRoot);
        Directory.CreateDirectory(serializationRoot);
        Directory.CreateDirectory(testsRoot);

        File.WriteAllText(
            Path.Combine(serializationRoot, "Franz.Common.Serialization.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(httpRoot, "Franz.Common.Http.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Franz.Common.Serialization\Franz.Common.Serialization.csproj" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(testsRoot, "Franz.Common.Http.Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\..\sources\Franz.Common.Http\Franz.Common.Http.csproj" />
              </ItemGroup>
            </Project>
            """);

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Single(context.ProductionDependencies);
        Assert.Single(context.TestDependencies);
        Assert.Equal(2, context.Dependencies.Count);
        Assert.Equal(2, context.ProductionModules.Count);
        Assert.Single(context.TestModules);
        Assert.DoesNotContain(context.Layers, layer => layer.Name == "Tests");
        Assert.Equal("Tests", Assert.Single(
            context.TestModules.Single().Layers).Name);

        var productionRelationship = Assert.Single(
            context.ProductionDependencies,
            dependency => dependency.Source == "Franz.Common.Http");
        Assert.Equal("Production", productionRelationship.Scope);
        Assert.Equal("Infrastructure", productionRelationship.SourceLayer);
        Assert.Equal("Infrastructure", productionRelationship.TargetLayer);
        Assert.Equal(
            Path.GetFullPath(httpRoot),
            productionRelationship.SourceProjectPath);
        Assert.Equal(
            Path.GetFullPath(serializationRoot),
            productionRelationship.TargetProjectPath);

        var testRelationship = Assert.Single(context.TestDependencies);
        Assert.Equal("Test", testRelationship.Scope);
        Assert.Equal("Tests", testRelationship.SourceLayer);
        Assert.Equal("Infrastructure", testRelationship.TargetLayer);
        Assert.Equal(
            Path.GetFullPath(testsRoot),
            testRelationship.SourceProjectPath);
        Assert.Equal(
            Path.GetFullPath(httpRoot),
            testRelationship.TargetProjectPath);
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
