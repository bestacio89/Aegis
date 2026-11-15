using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class AndroidSemanticAnalysisPlannerTests
{
    [Fact]
    public void Resolves_variant_owned_production_and_unit_test_roots_with_the_declared_platform()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sdkRoot = Path.Combine(root, "sdk");
        var modulePath = Path.Combine(root, "app");
        var mainRoot = Path.Combine(modulePath, "src", "main", "java");
        var testRoot = Path.Combine(modulePath, "src", "test", "java");
        var releaseRoot = Path.Combine(modulePath, "src", "release", "java");
        var androidJar = Path.Combine(sdkRoot, "platforms", "android-35", "android.jar");
        Directory.CreateDirectory(mainRoot);
        Directory.CreateDirectory(testRoot);
        Directory.CreateDirectory(releaseRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(androidJar)!);
        File.WriteAllText(androidJar, "android platform fixture");

        try
        {
            var module = new ProjectModuleContext
            {
                Name = "app",
                Path = modulePath,
                BuildSystem = ArchitectureBuildSystem.Gradle,
                Languages = ["Java"],
                GradleBuildModel = new GradleBuildModel
                {
                    IsComplete = true,
                    Android = new GradleAndroidBuildModel
                    {
                        PluginId = "com.android.application",
                        ModuleKind = "Application",
                        CompileSdk = "35",
                        IsComplete = true,
                        Variants =
                        [
                            new GradleAndroidVariant("debug", [], "debug"),
                            new GradleAndroidVariant("release", [], "release")
                        ],
                        SourceSets =
                        [
                            SourceSet("main", mainRoot, ProjectSourceRootOwnership.Production,
                                GradleAndroidTestKind.None, "debug", "release"),
                            SourceSet("test", testRoot, ProjectSourceRootOwnership.Test,
                                GradleAndroidTestKind.Unit, "debug", "release"),
                            SourceSet("release", releaseRoot, ProjectSourceRootOwnership.Production,
                                GradleAndroidTestKind.None, "release")
                        ]
                    }
                }
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "app",
                RootPath = root,
                Language = "Java",
                Modules = [module]
            };
            var planner = new AndroidSemanticAnalysisPlanner(
                new EmptyClasspathResolver(),
                new AndroidSdkResolver(sdkRoot, new Dictionary<string, string>()));

            var scopes = planner.ResolveScopes(module, context, "Java", null);

            Assert.Equal(4, scopes.Count);
            var debugProduction = Assert.Single(scopes, scope =>
                scope.VariantName == "debug" && !scope.IncludeTestCode);
            var debugTest = Assert.Single(scopes, scope =>
                scope.VariantName == "debug" && scope.IncludeTestCode);
            var releaseProduction = Assert.Single(scopes, scope =>
                scope.VariantName == "release" && !scope.IncludeTestCode);
            Assert.Contains(Path.GetFullPath(androidJar), debugProduction.ClasspathEntries);
            Assert.Contains(Path.GetFullPath(mainRoot), debugProduction.SourceRoots.Select(root => root.Directory));
            Assert.DoesNotContain(Path.GetFullPath(releaseRoot), debugProduction.SourceRoots.Select(root => root.Directory));
            Assert.Contains(Path.GetFullPath(testRoot), debugTest.SourceRoots.Select(root => root.Directory));
            Assert.Contains(Path.GetFullPath(releaseRoot), releaseProduction.SourceRoots.Select(root => root.Directory));
            Assert.All(scopes, scope => Assert.True(scope.IsComplete));
            Assert.Empty(context.DiscoveryDiagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static GradleAndroidSourceSet SourceSet(
        string name,
        string directory,
        ProjectSourceRootOwnership ownership,
        GradleAndroidTestKind testKind,
        params string[] variants) =>
        new()
        {
            Name = name,
            Ownership = ownership,
            TestKind = testKind,
            AppliesToVariants = [.. variants],
            JavaSourceDirectories = [directory]
        };

    private sealed class EmptyClasspathResolver : IJvmClasspathResolver
    {
        public JvmClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode) =>
            new([], [], false, true);
    }
}
