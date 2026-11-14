using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JvmModuleDependencyResolverTests
{
    [Fact]
    public void Orders_project_dependencies_before_consumers_and_keeps_test_edges_out_of_production_order()
    {
        var consumer = Module("a-consumer");
        var productionDependency = Module("m-production-dependency");
        var testSupport = Module("z-test-support");
        testSupport.IsTestSupport = true;
        var dependencies = new[]
        {
            Dependency(consumer, productionDependency, "Production"),
            Dependency(consumer, testSupport, "Test")
        };

        var productionOrder = JvmModuleDependencyResolver.OrderModules(
            [consumer, productionDependency],
            [consumer, productionDependency, testSupport],
            dependencies,
            includeTestDependencies: false,
            out var blockedProduction);
        var testOrder = JvmModuleDependencyResolver.OrderModules(
            [consumer, testSupport],
            [consumer, productionDependency, testSupport],
            dependencies,
            includeTestDependencies: true,
            out var blockedTest);

        Assert.Empty(blockedProduction);
        Assert.Empty(blockedTest);
        Assert.Equal(
            ["m-production-dependency", "a-consumer", "z-test-support"],
            productionOrder.Select(module => module.Name));
        Assert.Equal(
            ["z-test-support", "a-consumer"],
            testOrder.Select(module => module.Name));
    }

    [Fact]
    public void Resolves_transitive_production_and_test_dependency_paths_without_scope_leakage()
    {
        var consumer = Module("consumer");
        var productionDependency = Module("production-dependency");
        var transitiveProduction = Module("transitive-production");
        var testDependency = Module("test-dependency");
        var testDependencyProduction = Module("test-dependency-production");
        var transitiveTestDependency = Module("transitive-test-dependency");
        var dependencies = new[]
        {
            Dependency(consumer, productionDependency, "Production"),
            Dependency(productionDependency, transitiveProduction, "Production"),
            Dependency(consumer, testDependency, "Test"),
            Dependency(testDependency, testDependencyProduction, "Production"),
            Dependency(testDependency, transitiveTestDependency, "Test")
        };

        testDependency.IsTestSupport = true;
        transitiveTestDependency.IsTestSupport = true;
        var modules = new[]
        {
            consumer,
            productionDependency,
            transitiveProduction,
            testDependency,
            testDependencyProduction,
            transitiveTestDependency
        };
        var productionPaths = JvmModuleDependencyResolver.ResolveDependencyPaths(
            consumer.Path,
            modules,
            dependencies,
            includeTestDependencies: false);
        var testPaths = JvmModuleDependencyResolver.ResolveDependencyPaths(
            consumer.Path,
            modules,
            dependencies,
            includeTestDependencies: true);

        Assert.Equal(
            new[] { productionDependency.Path, transitiveProduction.Path }.Select(Path.GetFullPath),
            productionPaths.Production);
        Assert.Empty(productionPaths.Test);
        Assert.Equal(
            new[]
            {
                productionDependency.Path,
                testDependency.Path,
                transitiveProduction.Path,
                testDependencyProduction.Path,
                transitiveTestDependency.Path
            }.Select(Path.GetFullPath),
            testPaths.Production);
        Assert.Equal(
            new[] { testDependency.Path, transitiveTestDependency.Path }.Select(Path.GetFullPath),
            testPaths.Test);
    }

    [Fact]
    public void Orders_kotlin_modules_through_intermediate_java_modules()
    {
        var consumer = Module("a-kotlin-consumer");
        var javaIntermediate = Module("m-java-intermediate");
        var dependency = Module("z-kotlin-dependency");
        var dependencies = new[]
        {
            Dependency(consumer, javaIntermediate, "Production"),
            Dependency(javaIntermediate, dependency, "Production")
        };

        var order = JvmModuleDependencyResolver.OrderModules(
            [consumer, dependency],
            [consumer, javaIntermediate, dependency],
            dependencies,
            includeTestDependencies: false,
            out var blocked);

        Assert.Empty(blocked);
        Assert.Equal(
            ["z-kotlin-dependency", "a-kotlin-consumer"],
            order.Select(module => module.Name));
    }

    [Fact]
    public void Orders_test_support_modules_through_intermediate_test_support_modules()
    {
        var consumer = Module("a-kotlin-test-consumer");
        var javaTestSupport = Module("m-java-test-support");
        javaTestSupport.IsTestSupport = true;
        var kotlinTestSupport = Module("z-kotlin-test-support");
        kotlinTestSupport.IsTestSupport = true;
        var dependencies = new[]
        {
            Dependency(consumer, javaTestSupport, "Test"),
            Dependency(javaTestSupport, kotlinTestSupport, "Test")
        };

        var order = JvmModuleDependencyResolver.OrderModules(
            [consumer, kotlinTestSupport],
            [consumer, javaTestSupport, kotlinTestSupport],
            dependencies,
            includeTestDependencies: true,
            out var blocked);

        Assert.Empty(blocked);
        Assert.Equal(
            ["z-kotlin-test-support", "a-kotlin-test-consumer"],
            order.Select(module => module.Name));
    }

    [Fact]
    public void Test_scoped_dependency_to_regular_module_does_not_import_its_test_output()
    {
        var consumer = Module("consumer");
        var regularDependency = Module("regular-dependency");
        var dependencies = new[]
        {
            Dependency(consumer, regularDependency, "Test")
        };

        var paths = JvmModuleDependencyResolver.ResolveDependencyPaths(
            consumer.Path,
            [consumer, regularDependency],
            dependencies,
            includeTestDependencies: true);

        Assert.Equal([Path.GetFullPath(regularDependency.Path)], paths.Production);
        Assert.Empty(paths.Test);
    }

    [Fact]
    public void Blocks_cycles_and_modules_that_depend_on_a_cycle()
    {
        var first = Module("first");
        var second = Module("second");
        var dependent = Module("dependent");
        var dependencies = new[]
        {
            Dependency(first, second, "Production"),
            Dependency(second, first, "Production"),
            Dependency(dependent, first, "Production")
        };

        var order = JvmModuleDependencyResolver.OrderModules(
            [first, second, dependent],
            [first, second, dependent],
            dependencies,
            includeTestDependencies: false,
            out var blocked);

        Assert.Empty(order);
        Assert.Equal(
            new[] { first.Path, second.Path, dependent.Path }
                .Select(Path.GetFullPath)
                .OrderBy(path => path, PathComparer),
            blocked.OrderBy(path => path, PathComparer));
    }

    private static ProjectModuleContext Module(string name) =>
        new()
        {
            Name = name,
            Path = Path.Combine(Path.GetTempPath(), "aegis-jvm-dependency-order", name)
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

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
