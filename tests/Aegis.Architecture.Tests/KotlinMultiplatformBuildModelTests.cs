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
                        val webMain by creating {
                            dependsOn(commonMain)
                        }
                        val webTest by creating {
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
                            dependsOn(webMain)
                        }
                        val jsTest by getting {
                            dependsOn(webTest)
                        }
                        val wasmJsMain by getting {
                            dependsOn(webMain)
                        }
                        val wasmJsTest by getting {
                            dependsOn(webTest)
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
            Assert.Equal(16, multiplatform.SourceSets.Count);
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
                && sourceSet.DependsOn.SequenceEqual(["iosTest"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "wasmJsTest"
                && sourceSet.Kind == "TargetSpecific"
                && sourceSet.DependsOn.SequenceEqual(["webTest"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "commonMain"
                && sourceSet.Kind == "Common");
            Assert.True(multiplatform.IsComplete);
            Assert.Empty(multiplatform.Diagnostics);
            Assert.Contains(module.SourceRoots, sourceRoot =>
                Path.GetFullPath(sourceRoot.Directory) == Path.GetFullPath(
                    Path.Combine(root, "src", "commonMain", "kotlin"))
                && sourceRoot.Ownership == Aegis.Shared.Architecture.Enums.ProjectSourceRootOwnership.Production
                && sourceRoot.ContainsKotlinSources);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_preserves_custom_target_names_and_uses_declared_platform_families()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisKmpCustomTargets_{Guid.NewGuid():N}");
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
                    jvm("desktop")
                    iosArm64("iosDevice")
                }
                """);

            var source = Path.Combine(root, "src", "commonMain", "kotlin", "sample", "Common.kt");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package sample\nclass Common");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<GradleBuildModel>(module.GradleBuildModel);
            var multiplatform = Assert.IsType<KotlinMultiplatformBuildModel>(model.KotlinMultiplatform);

            Assert.Contains(multiplatform.Targets, target =>
                target.Name == "desktop" && target.Platform == "JVM");
            Assert.Contains(multiplatform.Targets, target =>
                target.Name == "iosDevice" && target.Platform == "Native");
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "desktopMain"
                && sourceSet.TargetName == "desktop"
                && sourceSet.DependsOn.SequenceEqual(["commonMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "iosDeviceMain"
                && sourceSet.TargetName == "iosDevice"
                && sourceSet.DependsOn.SequenceEqual(["iosMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "iosMain"
                && sourceSet.DependsOn.SequenceEqual(["appleMain"]));
            Assert.True(multiplatform.IsComplete);
            Assert.Empty(multiplatform.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_extracts_dependencies_from_each_kotlin_multiplatform_source_set()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisKmpDependencies_{Guid.NewGuid():N}");
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
                        commonMain.dependencies {
                            implementation("org.example:common-library:1.2.3")
                        }
                        val commonTest by getting {
                            dependencies {
                                implementation("org.example:test-library:2.3.4")
                            }
                        }
                        jvmMain.dependencies {
                            api("org.example:jvm-library:3.4.5")
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

            Assert.Contains(model.Dependencies, dependency =>
                dependency.Configuration == "commonMainImplementation"
                && dependency.Group == "org.example"
                && dependency.Artifact == "common-library");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Configuration == "commonTestImplementation"
                && dependency.Group == "org.example"
                && dependency.Artifact == "test-library");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Configuration == "jvmMainApi"
                && dependency.Group == "org.example"
                && dependency.Artifact == "jvm-library");
            Assert.True(multiplatform.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_models_and_discovers_explicit_kotlin_source_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisKmpCustomRoots_{Guid.NewGuid():N}");
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
                        val commonMain by getting {
                            kotlin.srcDir("src/shared-kotlin")
                        }
                    }
                }
                """);

            var sourceRoot = Path.Combine(root, "src", "shared-kotlin");
            var source = Path.Combine(sourceRoot, "sample", "Common.kt");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package sample\nclass Common");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<GradleBuildModel>(module.GradleBuildModel);
            var multiplatform = Assert.IsType<KotlinMultiplatformBuildModel>(model.KotlinMultiplatform);
            var commonMain = Assert.Single(multiplatform.SourceSets, sourceSet => sourceSet.Name == "commonMain");

            Assert.Equal([Path.GetFullPath(sourceRoot)], commonMain.KotlinSourceDirectories);
            Assert.Contains(module.SourceRoots, sourceRootModel =>
                Path.GetFullPath(sourceRootModel.Directory) == Path.GetFullPath(sourceRoot)
                && sourceRootModel.Ownership == ProjectSourceRootOwnership.Production
                && sourceRootModel.ContainsLanguage("Kotlin"));
            Assert.True(multiplatform.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_classifies_custom_intermediate_source_sets_from_explicit_dependency_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisKmpCustomHierarchy_{Guid.NewGuid():N}");
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
                    sourceSets {
                        val commonMain by getting
                        val commonTest by getting
                        val sharedMain by creating {
                            dependsOn(commonMain)
                        }
                        val sharedTest by creating {
                            dependsOn(commonTest)
                        }
                        val jvmMain by getting {
                            dependsOn(sharedMain)
                        }
                        val jvmTest by getting {
                            dependsOn(sharedTest)
                        }
                        val iosArm64Main by getting {
                            dependsOn(sharedMain)
                        }
                        val iosArm64Test by getting {
                            dependsOn(sharedTest)
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

            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "sharedMain"
                && sourceSet.Kind == "Intermediate"
                && sourceSet.DependsOn.SequenceEqual(["commonMain"]));
            Assert.Contains(multiplatform.SourceSets, sourceSet =>
                sourceSet.Name == "sharedTest"
                && sourceSet.Kind == "Intermediate"
                && sourceSet.DependsOn.SequenceEqual(["commonTest"]));
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
