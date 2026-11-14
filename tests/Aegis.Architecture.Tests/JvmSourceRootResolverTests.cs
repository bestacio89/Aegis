using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JvmSourceRootResolverTests
{
    [Fact]
    public void Regular_test_dependency_exposes_only_production_source_roots()
    {
        var workspace = Directory.CreateTempSubdirectory("aegis-jvm-roots-");
        try
        {
            var consumer = Module(workspace.FullName, "consumer");
            var dependency = Module(
                workspace.FullName,
                "dependency",
                ("src/main/java", ProjectSourceRootOwnership.Production),
                ("src/test/java", ProjectSourceRootOwnership.Test));
            var context = Context(
                workspace.FullName,
                [consumer, dependency],
                [Dependency(consumer, dependency, "Test")]);

            var roots = new JvmSourceRootResolver().ResolveReferencedSourceRoots(
                consumer,
                context,
                includeTestDependencies: true,
                language: "Java");

            Assert.Equal([Root(dependency, "src/main/java")], roots);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public void Test_support_dependency_exposes_its_production_and_test_source_roots()
    {
        var workspace = Directory.CreateTempSubdirectory("aegis-jvm-roots-");
        try
        {
            var consumer = Module(workspace.FullName, "consumer");
            var testSupport = Module(
                workspace.FullName,
                "test-support",
                ("src/main/java", ProjectSourceRootOwnership.Production),
                ("src/test/java", ProjectSourceRootOwnership.Test),
                isTestSupport: true);
            var context = Context(
                workspace.FullName,
                [consumer, testSupport],
                [Dependency(consumer, testSupport, "Test")]);

            var roots = new JvmSourceRootResolver().ResolveReferencedSourceRoots(
                consumer,
                context,
                includeTestDependencies: true,
                language: "Java");

            Assert.Equal(
                [
                    Root(testSupport, "src/main/java"),
                    Root(testSupport, "src/test/java")
                ],
                roots);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public void Regular_test_dependency_does_not_traverse_its_test_dependencies()
    {
        var workspace = Directory.CreateTempSubdirectory("aegis-jvm-roots-");
        try
        {
            var consumer = Module(workspace.FullName, "consumer");
            var regularDependency = Module(
                workspace.FullName,
                "regular-dependency",
                ("src/main/java", ProjectSourceRootOwnership.Production),
                ("src/test/java", ProjectSourceRootOwnership.Test));
            var transitiveProduction = Module(
                workspace.FullName,
                "transitive-production",
                ("src/main/java", ProjectSourceRootOwnership.Production));
            var transitiveTestSupport = Module(
                workspace.FullName,
                "transitive-test-support",
                ("src/main/java", ProjectSourceRootOwnership.Production),
                ("src/test/java", ProjectSourceRootOwnership.Test),
                isTestSupport: true);
            var context = Context(
                workspace.FullName,
                [consumer, regularDependency, transitiveProduction, transitiveTestSupport],
                [
                    Dependency(consumer, regularDependency, "Test"),
                    Dependency(regularDependency, transitiveProduction, "Production"),
                    Dependency(regularDependency, transitiveTestSupport, "Test")
                ]);

            var roots = new JvmSourceRootResolver().ResolveReferencedSourceRoots(
                consumer,
                context,
                includeTestDependencies: true,
                language: "Java");

            Assert.Equal(
                [
                    Root(regularDependency, "src/main/java"),
                    Root(transitiveProduction, "src/main/java")
                ],
                roots);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public void Test_support_dependency_traverses_transitive_test_support_dependencies()
    {
        var workspace = Directory.CreateTempSubdirectory("aegis-jvm-roots-");
        try
        {
            var consumer = Module(workspace.FullName, "consumer");
            var firstSupport = Module(
                workspace.FullName,
                "first-support",
                ("src/main/java", ProjectSourceRootOwnership.Production),
                ("src/test/java", ProjectSourceRootOwnership.Test),
                isTestSupport: true);
            var secondSupport = Module(
                workspace.FullName,
                "second-support",
                ("src/main/java", ProjectSourceRootOwnership.Production),
                ("src/test/java", ProjectSourceRootOwnership.Test),
                isTestSupport: true);
            var context = Context(
                workspace.FullName,
                [consumer, firstSupport, secondSupport],
                [
                    Dependency(consumer, firstSupport, "Test"),
                    Dependency(firstSupport, secondSupport, "Test")
                ]);

            var roots = new JvmSourceRootResolver().ResolveReferencedSourceRoots(
                consumer,
                context,
                includeTestDependencies: true,
                language: "Java");

            Assert.Equal(
                [
                    Root(firstSupport, "src/main/java"),
                    Root(firstSupport, "src/test/java"),
                    Root(secondSupport, "src/main/java"),
                    Root(secondSupport, "src/test/java")
                ],
                roots);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    [Fact]
    public void Production_source_roots_are_traversed_transitively_through_project_modules()
    {
        var workspace = Directory.CreateTempSubdirectory("aegis-jvm-roots-");
        try
        {
            var consumer = Module(workspace.FullName, "consumer");
            var intermediate = Module(
                workspace.FullName,
                "intermediate",
                ("src/main/java", ProjectSourceRootOwnership.Production),
                ("src/test/java", ProjectSourceRootOwnership.Test));
            var dependency = Module(
                workspace.FullName,
                "dependency",
                ("src/main/java", ProjectSourceRootOwnership.Production),
                ("src/test/java", ProjectSourceRootOwnership.Test));
            var context = Context(
                workspace.FullName,
                [consumer, intermediate, dependency],
                [
                    Dependency(consumer, intermediate, "Production"),
                    Dependency(intermediate, dependency, "Production")
                ]);

            var roots = new JvmSourceRootResolver().ResolveReferencedSourceRoots(
                consumer,
                context,
                includeTestDependencies: false,
                language: "Java");

            Assert.Equal(
                [
                    Root(intermediate, "src/main/java"),
                    Root(dependency, "src/main/java")
                ],
                roots);
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    private static ProjectModuleContext Module(
        string workspace,
        string name,
        params (string Directory, ProjectSourceRootOwnership Ownership)[] roots) =>
        Module(workspace, name, roots, isTestSupport: false, isTestProject: false);

    private static ProjectModuleContext Module(
        string workspace,
        string name,
        (string Directory, ProjectSourceRootOwnership Ownership)[] roots,
        bool isTestSupport = false,
        bool isTestProject = false)
    {
        var modulePath = Path.Combine(workspace, name);
        var sourceRoots = roots.Select(root =>
        {
            var directory = Path.Combine(modulePath, root.Directory);
            Directory.CreateDirectory(directory);
            return new ProjectSourceRoot
            {
                Directory = directory,
                Ownership = root.Ownership,
                Origin = ProjectSourceRootOrigin.Conventional,
                ContainsJavaSources = true,
                Languages = ["Java"]
            };
        }).ToList();

        return new ProjectModuleContext
        {
            Name = name,
            Path = modulePath,
            Languages = ["Java"],
            SourceRoots = sourceRoots,
            IsTestSupport = isTestSupport,
            IsTestProject = isTestProject
        };
    }

    private static ProjectArchitectureContext Context(
        string workspace,
        IReadOnlyCollection<ProjectModuleContext> modules,
        IReadOnlyCollection<ArchitectureDependencyContext> dependencies) =>
        new()
        {
            ProjectName = "jvm-source-roots",
            RootPath = workspace,
            Modules = modules.ToList(),
            Dependencies = dependencies.ToList()
        };

    private static ArchitectureDependencyContext Dependency(
        ProjectModuleContext source,
        ProjectModuleContext target,
        string scope) =>
        new()
        {
            Source = source.Name,
            SourceProjectPath = source.Path,
            Target = target.Name,
            TargetProjectPath = target.Path,
            DependencyType = "GradleProjectDependency",
            Scope = scope
        };

    private static string Root(ProjectModuleContext module, string relativePath) =>
        Path.Combine(module.Path, relativePath);
}
