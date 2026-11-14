using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class AndroidVariantDependencySelectorTests
{
    [Fact]
    public void Selects_base_flavor_build_type_and_variant_dependencies_for_production()
    {
        var module = CreateModule(
            Dependency("implementation", "base"),
            Dependency("api", "api"),
            Dependency("compileOnly", "compile-only"),
            Dependency("freeImplementation", "flavor"),
            Dependency("debugImplementation", "build-type"),
            Dependency("freeDebugImplementation", "variant"),
            Dependency("releaseImplementation", "other-variant"),
            Dependency("testImplementation", "unit-test"),
            Dependency("androidTestImplementation", "android-test"),
            Dependency("testFixturesImplementation", "test-fixtures"));

        var result = new AndroidVariantDependencySelector().Select(
            module, "freeDebug", AndroidVariantDependencyScope.Production);

        Assert.True(result.IsComplete);
        Assert.Equal(
            new string?[] { "base", "api", "compile-only", "flavor", "build-type", "variant" },
            result.Dependencies.Select(dependency => dependency.Artifact));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Unit_test_scope_includes_production_and_variant_unit_test_dependencies_only()
    {
        var module = CreateModule(
            Dependency("implementation", "base"),
            Dependency("freeDebugImplementation", "variant"),
            Dependency("testImplementation", "unit-test"),
            Dependency("testDebugImplementation", "build-type-unit-test"),
            Dependency("testFreeDebugImplementation", "variant-unit-test"),
            Dependency("androidTestImplementation", "android-test"),
            Dependency("testFixturesImplementation", "test-fixtures"));

        var result = new AndroidVariantDependencySelector().Select(
            module, "freeDebug", AndroidVariantDependencyScope.UnitTest);

        Assert.Equal(
            new string?[] { "base", "variant", "unit-test", "build-type-unit-test", "variant-unit-test" },
            result.Dependencies.Select(dependency => dependency.Artifact));
    }

    [Fact]
    public void Android_test_scope_does_not_leak_unit_test_or_test_fixture_dependencies()
    {
        var module = CreateModule(
            Dependency("implementation", "base"),
            Dependency("androidTestImplementation", "android-test"),
            Dependency("androidTestDebugImplementation", "build-type-android-test"),
            Dependency("androidTestFreeDebugImplementation", "variant-android-test"),
            Dependency("testImplementation", "unit-test"),
            Dependency("testFixturesImplementation", "test-fixtures"));

        var result = new AndroidVariantDependencySelector().Select(
            module, "freeDebug", AndroidVariantDependencyScope.AndroidTest);

        Assert.Equal(
            new string?[] { "base", "android-test", "build-type-android-test", "variant-android-test" },
            result.Dependencies.Select(dependency => dependency.Artifact));
    }

    [Fact]
    public void Test_fixture_scope_keeps_fixture_dependencies_separate()
    {
        var module = CreateModuleWithTestFixturesEnabled(
            Dependency("implementation", "base"),
            Dependency("testFixturesImplementation", "fixture"),
            Dependency("testFixturesDebugImplementation", "build-type-fixture"),
            Dependency("testFixturesFreeDebugImplementation", "variant-fixture"),
            Dependency("testImplementation", "unit-test"),
            Dependency("androidTestImplementation", "android-test"));

        var result = new AndroidVariantDependencySelector().Select(
            module, "freeDebug", AndroidVariantDependencyScope.TestFixtures);

        Assert.Equal(
            new string?[] { "base", "fixture", "build-type-fixture", "variant-fixture" },
            result.Dependencies.Select(dependency => dependency.Artifact));
    }

    [Fact]
    public void Does_not_select_test_fixture_dependencies_when_feature_is_disabled()
    {
        var module = CreateModule(Dependency("testFixturesImplementation", "fixture"));
        module.GradleBuildModel!.Android!.TestFixturesEnabled = false;

        var result = new AndroidVariantDependencySelector().Select(
            module, "freeDebug", AndroidVariantDependencyScope.TestFixtures);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Dependencies);
        Assert.Equal("AndroidTestFixturesDisabled", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Does_not_assume_test_fixtures_are_enabled_when_configuration_is_unknown()
    {
        var module = CreateModule(Dependency("testFixturesImplementation", "fixture"));

        var result = new AndroidVariantDependencySelector().Select(
            module, "freeDebug", AndroidVariantDependencyScope.TestFixtures);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Dependencies);
        Assert.Equal("AndroidTestFixturesAvailabilityUnknown", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void Rejects_variants_that_are_not_in_the_static_android_model()
    {
        var module = CreateModule(Dependency("implementation", "base"));

        var result = new AndroidVariantDependencySelector().Select(
            module, "release", AndroidVariantDependencyScope.Production);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Dependencies);
        Assert.Equal("AndroidVariantNotDeclared", Assert.Single(result.Diagnostics).Code);
        Assert.True(result.Diagnostics[0].AffectsCoverage);
    }

    [Fact]
    public void Preserves_partial_build_model_status_in_the_selection()
    {
        var module = CreateModule(Dependency("implementation", "base"), isComplete: false);

        var result = new AndroidVariantDependencySelector().Select(
            module, "freeDebug", AndroidVariantDependencyScope.Production);

        Assert.False(result.IsComplete);
        Assert.Single(result.Dependencies);
        Assert.Equal("AndroidVariantDependencyDeclarationsIncomplete", Assert.Single(result.Diagnostics).Code);
        Assert.True(result.Diagnostics[0].AffectsCoverage);
    }

    private static ProjectModuleContext CreateModuleWithTestFixturesEnabled(
        params GradleBuildModel.Dependency[] dependencies)
    {
        var module = CreateModule(dependencies);
        module.GradleBuildModel!.Android!.TestFixturesEnabled = true;
        return module;
    }

    private static ProjectModuleContext CreateModule(
        params GradleBuildModel.Dependency[] dependencies) =>
        CreateModule(dependencies, isComplete: true);

    private static ProjectModuleContext CreateModule(
        GradleBuildModel.Dependency[] dependencies,
        bool isComplete)
    {
        var android = new GradleAndroidBuildModel
        {
            PluginId = "com.android.application",
            Variants = [new GradleAndroidVariant("freeDebug", ["free"], "debug")],
            IsComplete = true
        };

        return new ProjectModuleContext
        {
            Path = Path.Combine(Path.GetTempPath(), "android-app"),
            GradleBuildModel = new GradleBuildModel
            {
                Android = android,
                Dependencies = [.. dependencies],
                IsComplete = isComplete
            }
        };
    }

    private static GradleBuildModel.Dependency Dependency(string configuration, string artifact) =>
        new(configuration, "org.example", artifact, "1.0.0", null, null,
            false, false, false, $"{configuration}(...)");
}
