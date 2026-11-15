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
                @"rootProject.name = ""sample""");
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
                        val iosTest by creating {
                            dependsOn(commonTest)
                        }
                        val iosArm64Main by getting {
                            dependsOn(iosMain)
                        }
                        val iosArm64Test by getting {
                            dependsOn(iosTest)
                        }
                        val iosSimulatorArm64Main by getting {
                            dependsOn(iosMain)
                        }
                        val iosSimulatorArm64Test by getting {
                            dependsOn(iosTest)
                        }
                        val jsMain by getting {
                            dependsOn(commonMain)
                        }
                        val jsTest by getting {
                            dependsOn(commonTest)
                        }
                        val wasmJsMain by getting {
                            dependsOn(commonMain)
                        }
                        val wasmJsTest by getting {
                            dependsOn(commonTest)
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

            Assert.Equal(new[] { "JVM", "Native", "Native", "JS", "Wasm" },
                multiplatform.Targets.Select(target => target.Platform).ToArray());
            Assert.Equal("Manual", multiplatform.HierarchyMode);
            Assert.Equal(14, multiplatform.SourceSets.Count);
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "jvmMain"
                && sourceSet.Ownership == "Production"
                && sourceSet.Kind == "TargetSpecific"
                && sourceSet.TargetName == "jvm"
                && sourceSet.DependsOn.SequenceEqual(["commonMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "jvmTest"
                && sourceSet.Ownership == "Test"
                && sourceSet.DependsOn.SequenceEqual(["commonTest"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "iosMain"
                && sourceSet.Kind == "Intermediate"
                && sourceSet.DependsOn.SequenceEqual(["commonMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "iosArm64Main"
                && sourceSet.Kind == "TargetSpecific"
                && sourceSet.TargetName == "iosArm64"
                && sourceSet.DependsOn.SequenceEqual(["iosMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "iosSimulatorArm64Test"
                && sourceSet.Kind == "TargetSpecific"
                && sourceSet.DependsOn.SequenceEqual(["commonTest"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "wasmJsTest"
                && sourceSet.Kind == "TargetSpecific"
                && sourceSet.DependsOn.SequenceEqual(["commonTest"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "commonMain"
                && sourceSet.Kind == "Common");
            Assert.True(multiplatform.IsComplete);
            Assert.Empty(multiplatform.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_infers_default_native_and_apple_hierarchy_without_fabricating_direct_edges()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisKmpDefaultHierarchy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                @"rootProject.name = ""sample""");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                plugins {
                    id("org.jetbrains.kotlin.multiplatform") version "2.4.21"
                }
                kotlin {
                    iosArm64()
                    iosSimulatorArm64()
                    macosArm64()
                    linuxX64()
                }
                """);

            var source = Path.Combine(root, "src", "commonMain", "kotlin", "sample", "Common.kt");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package sample\nclass Common");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var model = Assert.IsType<GradleBuildModel>(Assert.Single(context.Modules).GradleBuildModel);
            var multiplatform = Assert.IsType<KotlinMultiplatformBuildModel>(model.KotlinMultiplatform);

            Assert.Equal("Default", multiplatform.HierarchyMode);
            Assert.True(multiplatform.IsComplete);
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "iosArm64Main"
                && sourceSet.DependsOn.SequenceEqual(["iosMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "iosMain"
                && sourceSet.Kind == "Intermediate"
                && sourceSet.DependsOn.SequenceEqual(["appleMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "appleMain"
                && sourceSet.DependsOn.SequenceEqual(["nativeMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "nativeMain"
                && sourceSet.DependsOn.SequenceEqual(["commonMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "linuxX64Main"
                && sourceSet.DependsOn.SequenceEqual(["linuxMain"]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_reports_source_set_dependency_cycles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisKmpCycle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                @"rootProject.name = ""sample""");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                plugins {
                    id("org.jetbrains.kotlin.multiplatform") version "2.4.21"
                }
                kotlin {
                    jvm()
                    sourceSets {
                        val commonMain by creating {
                            dependsOn(jvmMain)
                        }
                        val jvmMain by getting {
                            dependsOn(commonMain)
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
                diagnostic.Code == "GradleKotlinMultiplatformSourceSetDependencyCycle");
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
                @"rootProject.name = ""sample""");
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
