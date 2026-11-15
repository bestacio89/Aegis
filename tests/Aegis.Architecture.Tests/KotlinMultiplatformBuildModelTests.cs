using Aegis.Architecture.Analysis.Detectors;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinMultiplatformBuildModelTests
{
    [Fact]
    public void Resolves_literal_targets_source_sets_and_depends_on_edges()
    {
        const string source = """
            plugins {
                kotlin("multiplatform") version "2.4.21"
            }

            kotlin {
                jvm()
                iosArm64()
                iosSimulatorArm64()
                js(IR) { browser() }
                wasmJs()

                sourceSets {
                    val commonMain by getting
                    val commonTest by getting
                    val jvmMain by getting {
                        dependsOn(commonMain)
                    }
                    val jvmTest by getting {
                        dependsOn(commonTest)
                    }
                    val iosMain by creating {
                        dependsOn(commonMain)
                    }
                }
            }
            """;

        var model = KotlinMultiplatformBuildModelResolver.Analyze(source, "/repo/build.gradle.kts");

        Assert.Equal(
            ["JVM", "Native", "Native", "JS", "Wasm"],
            model.Targets.Select(target => target.Platform));
        Assert.Equal(5, model.SourceSets.Count);
        Assert.Contains(model.SourceSets, sourceSet =>
            sourceSet.Name == "jvmMain"
            && sourceSet.Ownership == "Production"
            && sourceSet.DependsOn.SequenceEqual(["commonMain"]));
        Assert.Contains(model.SourceSets, sourceSet =>
            sourceSet.Name == "jvmTest"
            && sourceSet.Ownership == "Test"
            && sourceSet.DependsOn.SequenceEqual(["commonTest"]));
        Assert.Contains(model.SourceSets, sourceSet =>
            sourceSet.Name == "iosMain"
            && sourceSet.DependsOn.SequenceEqual(["commonMain"]));
        Assert.True(model.IsComplete);
        Assert.Empty(model.Diagnostics);
    }

    [Fact]
    public void Reports_unresolved_source_set_dependency_and_missing_target_declarations()
    {
        const string source = """
            kotlin {
                sourceSets {
                    val commonMain by getting
                    val jvmMain by getting {
                        dependsOn(missingSharedSet)
                    }
                }
            }
            """;

        var model = KotlinMultiplatformBuildModelResolver.Analyze(source, "/repo/build.gradle.kts");

        Assert.False(model.IsComplete);
        Assert.Contains(model.Diagnostics, diagnostic =>
            diagnostic.Code == "GradleKotlinMultiplatformSourceSetDependencyUnresolved");
        Assert.Contains(model.Diagnostics, diagnostic =>
            diagnostic.Code == "GradleKotlinMultiplatformTargetsNotStaticallyResolved");
    }

    [Fact]
    public void Reports_missing_kotlin_configuration_block()
    {
        var model = KotlinMultiplatformBuildModelResolver.Analyze(
            "plugins { kotlin(\"multiplatform\") }",
            "/repo/build.gradle.kts");

        Assert.False(model.IsComplete);
        Assert.Contains(model.Diagnostics, diagnostic =>
            diagnostic.Code == "GradleKotlinMultiplatformBlockNotStaticallyResolved");
    }
}
