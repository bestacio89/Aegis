using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Benchmarks.Workloads;

internal static class ArchitectureBenchmarkContextFactory
{
    public static ProjectArchitectureContext CreateProjectionContext(int componentCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(componentCount, 2);

        var modules = new List<ProjectModuleContext>(componentCount);
        var components = new List<ArchitectureComponentContext>(componentCount);
        var dependencies = new List<ArchitectureDependencyContext>(componentCount - 1);

        for (var index = 0; index < componentCount; index++)
        {
            var modulePath = $"/benchmark/module-{index:D5}";
            var packageName = $"com.aegis.module{index:D5}.application";

            modules.Add(new ProjectModuleContext { Name = $"module-{index:D5}", Path = modulePath });
            components.Add(new ArchitectureComponentContext
            {
                Id = $"component-{index:D5}",
                Name = packageName,
                QualifiedName = packageName,
                Language = "C#",
                ComponentKind = "Package",
                BuildModulePath = modulePath,
                ProductionSymbolCount = 10
            });

            if (index == 0)
                continue;

            dependencies.Add(new ArchitectureDependencyContext
            {
                Source = $"module-{index - 1:D5}",
                SourceProjectPath = $"/benchmark/module-{index - 1:D5}",
                Target = $"module-{index:D5}",
                TargetProjectPath = modulePath,
                DependencyType = "ProjectReference",
                Scope = "Production"
            });
        }

        return new ProjectArchitectureContext
        {
            ProjectName = "Aegis.PerformanceFixture",
            RootPath = "/benchmark",
            Modules = modules,
            ArchitectureComponents = components,
            Dependencies = dependencies
        };
    }

    public static void CreateDiscoveryFixture(string rootPath, int fileCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileCount, 1);

        Directory.CreateDirectory(Path.Combine(rootPath, "src"));
        File.WriteAllText(Path.Combine(rootPath, "BenchmarkFixture.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        for (var index = 0; index < fileCount; index++)
        {
            var source = $"namespace BenchmarkFixture.Module{index % 20};\n\npublic sealed class Component{index:D5} {{ public int Execute(int value) => value + {index}; }}";
            File.WriteAllText(Path.Combine(rootPath, "src", $"Component{index:D5}.cs"), source);
        }
    }
}
