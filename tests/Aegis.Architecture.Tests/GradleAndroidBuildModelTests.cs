using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class GradleAndroidBuildModelTests
{
    [Fact]
    public void Detect_extracts_android_application_configuration_and_variant_inputs_without_executing_gradle()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidGradle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"android-app\"");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                plugins {
                    id("com.android.application") version "9.0.0" apply false
                }
                """);
            File.WriteAllText(Path.Combine(root, "gradle.properties"),
                "android.builtInKotlin=true");
            File.WriteAllText(Path.Combine(root, "app.build.gradle.kts.fixture"), string.Empty);
            File.WriteAllText(Path.Combine(root, "build.gradle.kts.fixture"), string.Empty);
            File.WriteAllText(Path.Combine(root, "app", "build.gradle.kts"), """
                plugins {
                    id("com.android.application")
                }

                android {
                    namespace = "org.example.mobile"
                    compileSdk = 35
                    buildToolsVersion = "35.0.0"

                    defaultConfig {
                        applicationId = "org.example.mobile"
                        minSdk = 24
                        targetSdk = 35
                        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
                    }

                    compileOptions {
                        sourceCompatibility = JavaVersion.VERSION_17
                        targetCompatibility = JavaVersion.VERSION_17
                        isCoreLibraryDesugaringEnabled = true
                    }

                    flavorDimensions += "tier"
                    productFlavors {
                        create("free") {
                            dimension = "tier"
                            applicationIdSuffix = ".free"
                        }
                        create("paid") {
                            dimension = "tier"
                            applicationIdSuffix = ".paid"
                        }
                    }
                    buildTypes {
                        debug {
                            isDebuggable = true
                        }
                        release {
                            isMinifyEnabled = true
                        }
                    }
                }
                """);
            Directory.CreateDirectory(Path.Combine(root, "app", "src", "main", "kotlin"));
            File.WriteAllText(Path.Combine(root, "app", "src", "main", "kotlin", "Main.kt"),
                "package org.example.mobile\\nclass Main");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules, candidate => candidate.Path == Path.Combine(root, "app"));
            var model = Assert.IsType<GradleBuildModel>(module.GradleBuildModel);
            var android = Assert.IsType<GradleAndroidBuildModel>(model.Android);

            Assert.Equal("com.android.application", android.PluginId);
            Assert.Equal("9.0.0", android.PluginVersion);
            Assert.Equal("Application", android.ModuleKind);
            Assert.Equal("BuiltInDefault", android.KotlinIntegration);
            Assert.Equal("org.example.mobile", android.Namespace);
            Assert.Equal("35", android.CompileSdk);
            Assert.Equal("35.0.0", android.BuildToolsVersion);
            Assert.Equal("org.example.mobile", android.DefaultConfig!.ApplicationId);
            Assert.Equal("24", android.DefaultConfig.MinSdk);
            Assert.Equal("35", android.DefaultConfig.TargetSdk);
            Assert.Equal("androidx.test.runner.AndroidJUnitRunner", android.DefaultConfig.TestInstrumentationRunner);
            Assert.Equal("JavaVersion.VERSION_17", android.CompileOptions!.SourceCompatibility);
            Assert.Equal("JavaVersion.VERSION_17", android.CompileOptions.TargetCompatibility);
            Assert.True(android.CompileOptions.CoreLibraryDesugaringEnabled);
            Assert.Equal(["tier"], android.FlavorDimensions);
            Assert.Contains(android.ProductFlavors, flavor =>
                flavor.Name == "free" && flavor.Dimension == "tier" && flavor.ApplicationIdSuffix == ".free");
            Assert.Contains(android.ProductFlavors, flavor =>
                flavor.Name == "paid" && flavor.Dimension == "tier" && flavor.ApplicationIdSuffix == ".paid");
            Assert.Contains(android.BuildTypes, buildType => buildType.Name == "debug" && buildType.IsDebuggable == true);
            Assert.Contains(android.BuildTypes, buildType => buildType.Name == "release" && buildType.IsMinifyEnabled == true);
            Assert.DoesNotContain(android.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleAndroidLegacyKotlinPluginConflictsWithBuiltInKotlin");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_recognizes_legacy_kotlin_android_plugin_on_pre_agp_9_modules()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidLegacy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle"), "rootProject.name = 'android-legacy'");
            File.WriteAllText(Path.Combine(root, "build.gradle"), """
                plugins {
                    id 'com.android.library' version '8.7.3' apply false
                }
                """);
            var module = Path.Combine(root, "library");
            Directory.CreateDirectory(Path.Combine(module, "src", "main", "kotlin"));
            File.WriteAllText(Path.Combine(module, "build.gradle"), """
                plugins {
                    id 'com.android.library'
                    id 'org.jetbrains.kotlin.android' version '2.1.0'
                }
                android {
                    namespace 'org.example.library'
                    compileSdk 35
                }
                """);
            File.WriteAllText(Path.Combine(module, "src", "main", "kotlin", "Library.kt"), "class Library");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var discovered = Assert.Single(context.Modules, candidate => candidate.Path == module);
            var android = Assert.IsType<GradleAndroidBuildModel>(
                Assert.IsType<GradleBuildModel>(discovered.GradleBuildModel).Android);

            Assert.Equal("Library", android.ModuleKind);
            Assert.Equal("8.7.3", android.PluginVersion);
            Assert.Equal("LegacyKotlinAndroidPlugin", android.KotlinIntegration);
            Assert.True(android.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_reports_agp_9_legacy_kotlin_plugin_conflict_and_dynamic_sdk_configuration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidPartial_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"android-partial\"");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                plugins {
                    id("com.android.application") version "9.0.0" apply false
                }
                """);
            var module = Path.Combine(root, "app");
            Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "build.gradle.kts"), """
                plugins {
                    id("com.android.application")
                    id("org.jetbrains.kotlin.android") version "2.2.10"
                }
                android {
                    namespace = "org.example.app"
                    compileSdk = providers.gradleProperty("androidCompileSdk").get().toInt()
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var discovered = Assert.Single(context.Modules, candidate => candidate.Path == module);
            var android = Assert.IsType<GradleAndroidBuildModel>(
                Assert.IsType<GradleBuildModel>(discovered.GradleBuildModel).Android);

            Assert.False(android.IsComplete);
            Assert.Contains(android.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleAndroidLegacyKotlinPluginConflictsWithBuiltInKotlin");
            Assert.Contains(android.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleAndroidCompileSdkNotStaticallyResolved");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
