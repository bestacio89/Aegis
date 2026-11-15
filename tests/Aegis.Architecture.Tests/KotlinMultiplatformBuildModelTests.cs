using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class KotlinMultiplatformBuildModelTests
{
    [Fact]
    public void Detect_models_literal_targets_source_sets_and_depends_on_edges()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisKmpModel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"sample\"");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                plugins {
                    id("org.jetbrains.kotlin.multiplatform") version "2.4.21"
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
                """);
            var source = Path.Combine(root, "src", "commonMain", "kotlin", "sample", "Common.kt");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package sample\nclass Common");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<GradleBuildModel>(module.GradleBuildModel);
            var multiplatform = Assert.IsType<KotlinMultiplatformBuildModel>(model.KotlinMultiplatform);

            Assert.Equal(["JVM", "Native", "Native", "JS", "Wasm"],
                multiplatform.Targets.Select(target => target.Platform));
            Assert.Equal(5, multiplatform.SourceSets.Count);
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "jvmMain"
                && sourceSet.Ownership == "Production"
                && sourceSet.DependsOn.SequenceEqual(["commonMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "jvmTest"
                && sourceSet.Ownership == "Test"
                && sourceSet.DependsOn.SequenceEqual(["commonTest"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "iosMain"
                && sourceSet.DependsOn.SequenceEqual(["commonMain"]));
            Assert.True(multiplatform.IsComplete);
            Assert.Empty(multiplatform.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_reports_unresolved_source_set_dependencies_and_missing_targets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisKmpIncomplete_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"sample\"");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                plugins {
                    id("org.jetbrains.kotlin.multiplatform") version "2.4.21"
                }
                kotlin {
                    sourceSets {
                        val commonMain by getting
                        val jvmMain by getting {
                            dependsOn(missingSharedSet)
                        }
                    }
                }
                """);
            var source = Path.Combine(root, "src", "commonMain", "kotlin", "sample", "Common.kt");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package sample\nclass Common");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var model = Assert.IsType<GradleBuildModel>(Assert.Single(context.Modules).GradleBuildModel);
            var multiplatform = Assert.IsType<KotlinMultiplatformBuildModel>(model.KotlinMultiplatform);

            Assert.False(multiplatform.IsComplete);
            Assert.Contains(multiplatform.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleKotlinMultiplatformSourceSetDependencyUnresolved");
            Assert.Contains(multiplatform.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleKotlinMultiplatformTargetsNotStaticallyResolved");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
