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
            var appDirectory = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
            File.WriteAllText(Path.Combine(appDirectory, "build.gradle.kts"), """
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
            Directory.CreateDirectory(Path.Combine(appDirectory, "src", "main", "kotlin"));
            File.WriteAllText(Path.Combine(appDirectory, "src", "main", "kotlin", "Main.kt"),
                "package org.example.mobile\\nclass Main");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var rootModule = Assert.Single(context.Modules, candidate => candidate.Path == root);
            var rootGradleModel = Assert.IsType<GradleBuildModel>(rootModule.GradleBuildModel);
            Assert.Null(rootGradleModel.Android);
            Assert.Contains(rootGradleModel.Plugins, plugin =>
                plugin.Id == "com.android.application" && !plugin.IsApplied);
            var module = Assert.Single(context.Modules, candidate => candidate.Path == appDirectory);
            var model = Assert.IsType<GradleBuildModel>(module.GradleBuildModel);
            var android = Assert.IsType<GradleAndroidBuildModel>(model.Android);

            Assert.Equal("com.android.application", android.PluginId);
            Assert.Equal("9.0.0", android.PluginVersion);
            Assert.Equal("Application", android.ModuleKind);
            Assert.Equal("AndroidApplication", module.ProjectKind);
            Assert.True(module.IsExecutable);
            Assert.Contains("Android", module.Frameworks);
            Assert.Equal("BuiltInDefault", android.KotlinIntegration);
            Assert.True(android.NewDslEnabled);
            Assert.Equal("org.example.mobile", android.Namespace);
            Assert.Equal("35", android.CompileSdk);
            Assert.Equal("35.0.0", android.BuildToolsVersion);
            Assert.Equal("org.example.mobile", android.DefaultConfig!.ApplicationId);
            Assert.Equal("24", android.DefaultConfig.MinSdk);
            Assert.Equal("35", android.DefaultConfig.TargetSdk);
            Assert.Equal("androidx.test.runner.AndroidJUnitRunner", android.DefaultConfig.TestInstrumentationRunner);
            Assert.Equal("JavaVersion.VERSION_17", android.CompileOptions!.SourceCompatibility);
            Assert.Equal("JavaVersion.VERSION_17", android.CompileOptions.TargetCompatibility);
            Assert.Equal(true, android.CompileOptions.CoreLibraryDesugaringEnabled);
            Assert.Equal(new[] { "tier" }, android.FlavorDimensions);
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
    public void Detect_supports_legacy_apply_plugin_syntax_and_root_buildscript_versions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidLegacySyntax_{Guid.NewGuid():N}");
        var module = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(module, "src", "main", "kotlin"));
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle"),
                "rootProject.name = 'android-legacy-syntax'");
            File.WriteAllText(Path.Combine(root, "build.gradle"), """
                buildscript {
                    repositories { google(); mavenCentral() }
                    dependencies {
                        classpath 'com.android.tools.build:gradle:8.7.3'
                        classpath 'org.jetbrains.kotlin:kotlin-gradle-plugin:2.1.0'
                    }
                }
                """);
            File.WriteAllText(Path.Combine(module, "build.gradle"), """
                apply plugin: 'com.android.application'
                apply plugin: 'kotlin-android'
                android {
                    namespace 'org.example.legacy'
                    compileSdkVersion 35
                }
                """);
            File.WriteAllText(Path.Combine(module, "src", "main", "kotlin", "Legacy.kt"),
                "package org.example.legacy\nclass Legacy");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var discovered = Assert.Single(context.Modules, candidate => candidate.Path == module);
            var gradle = Assert.IsType<GradleBuildModel>(discovered.GradleBuildModel);
            var android = Assert.IsType<GradleAndroidBuildModel>(gradle.Android);

            Assert.Equal("AndroidApplication", discovered.ProjectKind);
            Assert.Equal("com.android.application", android.PluginId);
            Assert.Equal("8.7.3", android.PluginVersion);
            Assert.Equal("LegacyKotlinAndroidPlugin", android.KotlinIntegration);
            Assert.Equal("2.1.0", gradle.KotlinPluginVersion);
            Assert.True(android.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_keeps_android_dsl_modules_out_of_jvm_analysis_when_plugin_identity_is_unknown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidUnknownPlugin_{Guid.NewGuid():N}");
        var module = Path.Combine(root, "app");
        Directory.CreateDirectory(module);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"android-unknown-plugin\"");
            File.WriteAllText(Path.Combine(module, "build.gradle.kts"), """
                android {
                    namespace = "org.example.unknown"
                    compileSdk = 35
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var discovered = Assert.Single(context.Modules, candidate => candidate.Path == module);
            var android = Assert.IsType<GradleAndroidBuildModel>(
                Assert.IsType<GradleBuildModel>(discovered.GradleBuildModel).Android);

            Assert.Equal("Unknown", android.ModuleKind);
            Assert.Equal(string.Empty, android.PluginId);
            Assert.False(android.IsComplete);
            Assert.Contains(android.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleAndroidPluginNotStaticallyResolved");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_does_not_treat_dynamic_agp_versions_as_resolved()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidDynamicAgp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"android-dynamic-agp\"");
            var module = Path.Combine(root, "app");
            Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "build.gradle.kts"), """
                plugins {
                    id("com.android.application") version "$agpVersion"
                }
                android {
                    namespace = "org.example.dynamic"
                    compileSdk = 35
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var discovered = Assert.Single(context.Modules, candidate => candidate.Path == module);
            var android = Assert.IsType<GradleAndroidBuildModel>(
                Assert.IsType<GradleBuildModel>(discovered.GradleBuildModel).Android);

            Assert.Null(android.PluginVersion);
            Assert.Equal("Unknown", android.KotlinIntegration);
            Assert.False(android.IsComplete);
            Assert.Contains(android.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleAndroidPluginVersionNotStaticallyResolved");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_honors_per_module_builtin_kotlin_disable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidKotlinDisabled_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"android-kotlin-disabled\"");
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
                }
                android {
                    namespace = "org.example.disabled"
                    compileSdk = 35
                    enableKotlin = false
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var discovered = Assert.Single(context.Modules, candidate => candidate.Path == module);
            var android = Assert.IsType<GradleAndroidBuildModel>(
                Assert.IsType<GradleBuildModel>(discovered.GradleBuildModel).Android);

            Assert.Equal("Disabled", android.KotlinIntegration);
            Assert.True(android.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_resolves_android_plugin_alias_from_the_version_catalog()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidCatalog_{Guid.NewGuid():N}");
        var module = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(root, "gradle"));
        Directory.CreateDirectory(module);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"android-catalog\"");
            File.WriteAllText(Path.Combine(root, "gradle", "libs.versions.toml"), """
                [versions]
                agp = "9.0.0"

                [plugins]
                android-application = { id = "com.android.application", version.ref = "agp" }
                """);
            File.WriteAllText(Path.Combine(module, "build.gradle.kts"), """
                plugins {
                    alias(libs.plugins.android.application)
                }
                android {
                    namespace = "org.example.catalog"
                    compileSdk = 35
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var discovered = Assert.Single(context.Modules, candidate => candidate.Path == module);
            var android = Assert.IsType<GradleAndroidBuildModel>(
                Assert.IsType<GradleBuildModel>(discovered.GradleBuildModel).Android);

            Assert.Equal("com.android.application", android.PluginId);
            Assert.Equal("9.0.0", android.PluginVersion);
            Assert.Equal("BuiltInDefault", android.KotlinIntegration);
            Assert.True(android.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_honors_agp_9_builtin_kotlin_opt_out_for_legacy_plugin_modules()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidOptOut_{Guid.NewGuid():N}");
        var module = Path.Combine(root, "app");
        Directory.CreateDirectory(module);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle"), "rootProject.name = 'android-opt-out'");
            File.WriteAllText(Path.Combine(root, "gradle.properties"), """
                android.builtInKotlin=false
                android.newDsl=false
                """);
            File.WriteAllText(Path.Combine(root, "build.gradle"), """
                plugins {
                    id 'com.android.application' version '9.0.0' apply false
                }
                """);
            File.WriteAllText(Path.Combine(module, "build.gradle"), """
                plugins {
                    id 'com.android.application'
                    id 'org.jetbrains.kotlin.android' version '2.2.10'
                }
                android {
                    namespace 'org.example.optout'
                    compileSdk 35
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var discovered = Assert.Single(context.Modules, candidate => candidate.Path == module);
            var android = Assert.IsType<GradleAndroidBuildModel>(
                Assert.IsType<GradleBuildModel>(discovered.GradleBuildModel).Android);

            Assert.Equal("LegacyKotlinAndroidPlugin", android.KotlinIntegration);
            Assert.Equal(false, android.NewDslEnabled);
            Assert.DoesNotContain(android.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleAndroidLegacyKotlinPluginConflictsWithBuiltInKotlin");
            Assert.True(android.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_reports_conflicting_android_module_plugins()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidConflict_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"android-conflict\"");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                plugins {
                    id("com.android.application") version "8.7.3" apply false
                }
                """);
            var module = Path.Combine(root, "app");
            Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "build.gradle.kts"), """
                plugins {
                    id("com.android.application")
                    id("com.android.library")
                }
                android {
                    namespace = "org.example.conflict"
                    compileSdk = 35
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var discovered = Assert.Single(context.Modules, candidate => candidate.Path == module);
            var android = Assert.IsType<GradleAndroidBuildModel>(
                Assert.IsType<GradleBuildModel>(discovered.GradleBuildModel).Android);

            Assert.False(android.IsComplete);
            Assert.Contains(android.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleAndroidPluginConflict");
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
            Assert.Equal("AndroidLibrary", discovered.ProjectKind);
            Assert.True(discovered.IsLibrary);
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
            Assert.True(android.NewDslEnabled);
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

    [Fact]
    public void Detect_builds_android_variant_matrix_and_separates_production_unit_and_instrumentation_source_sets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidVariants_{Guid.NewGuid():N}");
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"android-variants\"");
            File.WriteAllText(Path.Combine(app, "build.gradle.kts"), """
                plugins {
                    id("com.android.application") version "8.7.3"
                    id("org.jetbrains.kotlin.android") version "2.1.0"
                }
                android {
                    namespace = "org.example.variants"
                    compileSdk = 35
                    flavorDimensions += "tier"
                    productFlavors {
                        create("free") { dimension = "tier" }
                        create("paid") { dimension = "tier" }
                    }
                    buildTypes {
                        debug { isDebuggable = true }
                        release { isMinifyEnabled = true }
                        create("staging") { }
                    }
                }
                """);

            WriteFile(app, "src/main/java/Main.java", "package org.example; class Main {}");
            WriteFile(app, "src/main/java/Main.kt", "package org.example\nclass MainKotlin");
            WriteFile(app, "src/free/java/FreeFlavor.java", "package org.example; class FreeFlavor {}");
            WriteFile(app, "src/freeDebug/kotlin/FreeDebug.kt", "package org.example\nclass FreeDebug");
            WriteFile(app, "src/testDebug/kotlin/DebugUnitTest.kt", "package org.example\nclass DebugUnitTest");
            WriteFile(app, "src/androidTest/java/InstrumentedTest.kt", "package org.example\nclass InstrumentedTest");
            WriteFile(app, "src/main/AndroidManifest.xml", "<manifest />");
            WriteFile(app, "src/freeDebug/AndroidManifest.xml", "<manifest />");
            WriteFile(app, "src/freeDebug/res/values/strings.xml", "<resources />");
            WriteFile(app, "src/freeDebug/assets/config.json", "{}");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules, candidate => candidate.Path == app);
            var android = Assert.IsType<GradleAndroidBuildModel>(
                Assert.IsType<GradleBuildModel>(module.GradleBuildModel).Android);

            Assert.Contains(android.Variants, variant =>
                variant.Name == "freeDebug"
                && variant.ProductFlavors.SequenceEqual(["free"])
                && variant.BuildType == "debug");
            Assert.Contains(android.Variants, variant => variant.Name == "freeRelease");
            Assert.Contains(android.Variants, variant => variant.Name == "paidDebug");
            Assert.Contains(android.Variants, variant => variant.Name == "paidRelease");
            Assert.Contains(android.Variants, variant => variant.Name == "freeStaging");
            Assert.Equal(["freeDebug"], android.SourceSets.Single(sourceSet => sourceSet.Name == "freeDebug").AppliesToVariants);

            var main = Assert.Single(android.SourceSets, sourceSet => sourceSet.Name == "main");
            Assert.Contains(main.ManifestFiles, file => file.EndsWith(
                Path.Combine("src", "main", "AndroidManifest.xml"), StringComparison.Ordinal));
            Assert.Single(main.JavaSourceDirectories);
            Assert.Single(main.KotlinSourceDirectories);

            var freeDebug = Assert.Single(android.SourceSets, sourceSet => sourceSet.Name == "freeDebug");
            Assert.Contains(freeDebug.ManifestFiles, file => file.EndsWith(
                Path.Combine("src", "freeDebug", "AndroidManifest.xml"), StringComparison.Ordinal));
            Assert.Contains(freeDebug.ResourceDirectories, directory => directory.EndsWith(
                Path.Combine("src", "freeDebug", "res"), StringComparison.Ordinal));
            Assert.Contains(freeDebug.AssetDirectories, directory => directory.EndsWith(
                Path.Combine("src", "freeDebug", "assets"), StringComparison.Ordinal));

            var instrumentation = Assert.Single(android.SourceSets, sourceSet => sourceSet.Name == "androidTest");
            Assert.Equal(Aegis.Shared.Architecture.Enums.ProjectSourceRootOwnership.Test, instrumentation.Ownership);
            Assert.Equal(Aegis.Shared.Architecture.Enums.GradleAndroidTestKind.Instrumentation, instrumentation.TestKind);
            Assert.Equal(android.Variants.Select(variant => variant.Name).OrderBy(name => name), instrumentation.AppliesToVariants.OrderBy(name => name));
            var unitTests = Assert.Single(android.SourceSets, sourceSet => sourceSet.Name == "testDebug");
            Assert.Equal(Aegis.Shared.Architecture.Enums.ProjectSourceRootOwnership.Test, unitTests.Ownership);
            Assert.Equal(Aegis.Shared.Architecture.Enums.GradleAndroidTestKind.Unit, unitTests.TestKind);
            Assert.Equal(
                android.Variants.Where(variant => variant.BuildType == "debug").Select(variant => variant.Name).OrderBy(name => name),
                unitTests.AppliesToVariants.OrderBy(name => name));

            var mainRoot = Assert.Single(module.SourceRoots, sourceRoot =>
                sourceRoot.Directory == Path.Combine(app, "src", "main", "java"));
            Assert.Contains("Java", mainRoot.Languages);
            Assert.Contains("Kotlin", mainRoot.Languages);
            Assert.Contains(module.SourceRoots, sourceRoot =>
                sourceRoot.Directory == Path.Combine(app, "src", "androidTest", "java")
                && sourceRoot.Ownership == Aegis.Shared.Architecture.Enums.ProjectSourceRootOwnership.Test);
            Assert.Contains(module.SourceRoots, sourceRoot =>
                sourceRoot.Directory == Path.Combine(app, "src", "freeDebug", "kotlin")
                && sourceRoot.Ownership == Aegis.Shared.Architecture.Enums.ProjectSourceRootOwnership.Production);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_reads_literal_android_source_set_directory_and_manifest_overrides()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisAndroidExplicitSourceSets_{Guid.NewGuid():N}");
        var app = Path.Combine(root, "app");
        Directory.CreateDirectory(app);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"android-explicit-source-sets\"");
            File.WriteAllText(Path.Combine(app, "build.gradle.kts"), """
                plugins {
                    id("com.android.application") version "8.7.3"
                    id("org.jetbrains.kotlin.android") version "2.1.0"
                }
                android {
                    namespace = "org.example.explicit"
                    compileSdk = 35
                    sourceSets {
                        getByName("main") {
                            java.srcDirs("src/shared/java")
                            kotlin.srcDirs("src/shared/kotlin")
                            manifest.srcFile("manifests/main/AndroidManifest.xml")
                            res.srcDirs("resources/main")
                            assets.srcDirs("assets/main")
                        }
                    }
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules, candidate => candidate.Path == app);
            var android = Assert.IsType<GradleAndroidBuildModel>(
                Assert.IsType<GradleBuildModel>(module.GradleBuildModel).Android);
            var main = Assert.Single(android.SourceSets, sourceSet => sourceSet.Name == "main");

            Assert.Contains(main.JavaSourceDirectories, directory => directory == Path.Combine(app, "src", "shared", "java"));
            Assert.Contains(main.KotlinSourceDirectories, directory => directory == Path.Combine(app, "src", "shared", "kotlin"));
            Assert.Contains(main.ManifestFiles, file => file == Path.Combine(app, "manifests", "main", "AndroidManifest.xml"));
            Assert.Contains(main.ResourceDirectories, directory => directory == Path.Combine(app, "resources", "main"));
            Assert.Contains(main.AssetDirectories, directory => directory == Path.Combine(app, "assets", "main"));
            Assert.Contains(module.SourceRoots, sourceRoot =>
                sourceRoot.Directory == Path.Combine(app, "src", "shared", "java")
                && sourceRoot.Ownership == Aegis.Shared.Architecture.Enums.ProjectSourceRootOwnership.Production);
            Assert.Contains(module.SourceRoots, sourceRoot =>
                sourceRoot.Directory == Path.Combine(app, "src", "shared", "kotlin")
                && sourceRoot.Ownership == Aegis.Shared.Architecture.Enums.ProjectSourceRootOwnership.Production);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
