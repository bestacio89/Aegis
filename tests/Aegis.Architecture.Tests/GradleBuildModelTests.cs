using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Enums;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class GradleBuildModelTests
{
    [Fact]
    public void Detect_extracts_literal_plugins_dependencies_catalog_and_toolchain_without_execution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleModel_{Guid.NewGuid():N}");
        var service = Path.Combine(root, "service");
        var catalogDirectory = Path.Combine(root, "gradle");
        Directory.CreateDirectory(service);
        Directory.CreateDirectory(catalogDirectory);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"sample\"\ninclude(\":service\")");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"),
                "plugins { id(\"java\") }");
            var buildFile = Path.Combine(service, "build.gradle.kts");
            File.WriteAllText(buildFile, """
                plugins {
                    alias(libs.plugins.mapstruct)
                    alias(libs.plugins.spring.boot)
                }
                java {
                    toolchain {
                        languageVersion.set(JavaLanguageVersion.of(21))
                    }
                }
                dependencies {
                    implementation("org.springframework:spring-context:6.2.8")
                    testImplementation("org.junit.jupiter:junit-jupiter-api:5.12.2")
                    annotationProcessor(libs.mapstruct)
                    implementation(platform("org.springframework.boot:spring-boot-dependencies:3.4.1"))
                    implementation(project(":api"))
                }
                """);
            File.WriteAllText(Path.Combine(catalogDirectory, "libs.versions.toml"),
                """
                [versions]
                mapstruct = "1.6.3"
                springBoot = "3.4.1"

                [libraries]
                mapstruct = { module = "org.mapstruct:mapstruct", version.ref = "mapstruct" }

                [plugins]
                mapstruct = { id = "org.mapstruct", version = "1.6.3" }
                spring-boot = { id = "org.springframework.boot", version.ref = "springBoot" }
                """);
            var source = Path.Combine(service, "src", "main", "java", "example", "Service.java");
            var testSource = Path.Combine(service, "src", "test", "java", "example", "ServiceTest.java");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(testSource)!);
            File.WriteAllText(source, "package example; public final class Service {}");
            File.WriteAllText(testSource, "package example; public final class ServiceTest {}");

            var context = ProjectArchitectureContextDetector.Detect(root);

            var module = Assert.Single(context.Modules, candidate => candidate.Path == service);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);
            Assert.Equal("Kotlin", model.Dialect);
            Assert.Equal("21", model.JavaToolchainVersion);
            Assert.Contains(model.Plugins, plugin => plugin.Id == "org.springframework.boot" && plugin.Version == "3.4.1");
            Assert.Contains(model.Plugins, plugin => plugin.Id == "org.mapstruct" && plugin.Version == "1.6.3");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Group == "org.springframework" && dependency.Artifact == "spring-context");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Group == "org.junit.jupiter" && dependency.Artifact == "junit-jupiter-api"
                && dependency.Configuration == "testImplementation");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Group == "org.mapstruct" && dependency.Artifact == "mapstruct"
                && dependency.IsAnnotationProcessor);
            Assert.Contains(model.Dependencies, dependency => dependency.IsPlatform
                && dependency.Group == "org.springframework.boot");
            Assert.Contains(model.Dependencies, dependency => dependency.ProjectPath == ":api");
            Assert.Contains(model.SourceSets, sourceSet => sourceSet.Name == "main" && !sourceSet.IsTest);
            Assert.Contains(model.SourceSets, sourceSet => sourceSet.Name == "test" && sourceSet.IsTest);
            Assert.Contains(context.DependencyEvidence, dependency =>
                dependency.Ecosystem == "Gradle" && dependency.Name == "org.junit.jupiter:junit-jupiter-api"
                && dependency.IsTestOnly && dependency.ResolvedVersion is null);
            Assert.Contains("org.springframework:spring-context", context.DetectedDependencies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_expands_catalog_bundles_and_resolves_rich_version_constraints()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleRichCatalog_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "gradle"));
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"rich-catalog\"");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                plugins {
                    alias(libs.plugins.example)
                }
                dependencies {
                    implementation(libs.bundles.web)
                }
                """);
            File.WriteAllText(Path.Combine(root, "gradle", "libs.versions.toml"), """
                [versions]
                spring = { strictly = "6.2.8", prefer = "6.2.9", reject = ["6.2.7"] }
                junit = { require = "5.12.2" }
                plugin = { prefer = "1.4.0" }

                [libraries]
                spring-core = { module = "org.springframework:spring-core", version.ref = "spring" }
                spring-web = { module = "org.springframework:spring-web", version = { strictly = "6.2.8", prefer = "6.2.9" } }
                junit-api = { module = "org.junit.jupiter:junit-jupiter-api", version.ref = "junit" }

                [bundles]
                web = ["spring-core", "spring-web", "junit-api"]

                [plugins]
                example = { id = "org.example.plugin", version.ref = "plugin" }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);

            Assert.Equal(3, model.Dependencies.Count);
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Artifact == "spring-core" && dependency.Version == "6.2.8"
                && dependency.CatalogAlias == "spring.core");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Artifact == "spring-web" && dependency.Version == "6.2.8");
            Assert.Contains(model.Dependencies, dependency =>
                dependency.Artifact == "junit-jupiter-api" && dependency.Version == "5.12.2");
            Assert.Contains(model.Plugins, plugin =>
                plugin.Id == "org.example.plugin" && plugin.Version == "1.4.0");
            Assert.True(model.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_reports_missing_catalog_bundle_members_and_version_references()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleBrokenCatalog_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "gradle"));
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle"), "rootProject.name = 'broken-catalog'");
            File.WriteAllText(Path.Combine(root, "build.gradle"), """
                dependencies {
                    implementation(libs.bundles.web)
                }
                """);
            File.WriteAllText(Path.Combine(root, "gradle", "libs.versions.toml"), """
                [libraries]
                known = { module = "org.example:known", version.ref = "missing-version" }

                [bundles]
                web = ["known", "missing-library"]
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);

            Assert.False(model.IsComplete);
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleVersionCatalogVersionReferenceNotResolved");
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleVersionCatalogBundleMemberNotResolved");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_marks_dynamic_dependency_declarations_as_incomplete()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleDynamic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle"), "rootProject.name = 'sample'");
            File.WriteAllText(Path.Combine(root, "build.gradle"),
                "dependencies {\n implementation(\"org.example:library:$version\")\n}");
            var source = Path.Combine(root, "src", "main", "java", "example", "App.java");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "package example; public final class App {}");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            Assert.NotNull(module.GradleBuildModel);
            Assert.False(module.GradleBuildModel.IsComplete);
            Assert.Contains(module.GradleBuildModel.Diagnostics,
                diagnostic => diagnostic.Code == "GradleDependencyNotStaticallyResolved");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    
    [Fact]
    public void Detect_marks_multiline_version_catalog_entries_incomplete_without_fabricating_aliases()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleCatalog_{Guid.NewGuid():N}");
        var catalogDirectory = Path.Combine(root, "gradle");
        Directory.CreateDirectory(catalogDirectory);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"catalog-sample\"");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                dependencies {
                    implementation(libs.mapstruct)
                }
                """);
            File.WriteAllText(Path.Combine(catalogDirectory, "libs.versions.toml"), """
                [libraries]
                mapstruct = {
                    module = "org.mapstruct:mapstruct",
                    version = "1.6.3"
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);

            Assert.False(model.IsComplete);
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleVersionCatalogEntryUnsupported");
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleDependencyAliasNotResolved");
            Assert.DoesNotContain(model.Dependencies, dependency => dependency.Artifact == "mapstruct");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void Detect_classifies_gradle_generated_sources_only_when_source_set_ownership_is_evident()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleGenerated_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"), "rootProject.name = \"generated-fixture\"");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), "plugins { id(\"java\") }");

            WriteJavaSource(root, "src/main/java/example/Production.java");
            WriteJavaSource(root, "src/test/java/example/ProductionTest.java");
            WriteJavaSource(root, "build/generated/sources/annotationProcessor/java/main/example/GeneratedMapper.java");
            WriteJavaSource(root, "build/generated/sources/annotationProcessor/java/test/example/GeneratedTestMapper.java");
            WriteJavaSource(root, "build/generated/sources/opaque/example/UnknownOwnership.java");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);

            // Generated build output is inspected by the Gradle-specific resolver,
            // but remains excluded from the repository-wide governed-file count.
            Assert.Equal(4, context.FileCount);

            Assert.Contains(model.SourceRoots, sourceRoot =>
                sourceRoot.Origin == ProjectSourceRootOrigin.Conventional
                && sourceRoot.Ownership == ProjectSourceRootOwnership.Production
                && !sourceRoot.IsGenerated
                && sourceRoot.Directory == Path.Combine(root, "src", "main", "java"));
            Assert.Contains(model.SourceRoots, sourceRoot =>
                sourceRoot.Origin == ProjectSourceRootOrigin.GeneratedConvention
                && sourceRoot.Ownership == ProjectSourceRootOwnership.Production
                && sourceRoot.IsGenerated
                && sourceRoot.Directory == Path.Combine(root, "build", "generated", "sources", "annotationProcessor", "java", "main"));
            Assert.Contains(model.SourceRoots, sourceRoot =>
                sourceRoot.Origin == ProjectSourceRootOrigin.GeneratedConvention
                && sourceRoot.Ownership == ProjectSourceRootOwnership.Test
                && sourceRoot.IsGenerated
                && sourceRoot.Directory == Path.Combine(root, "build", "generated", "sources", "annotationProcessor", "java", "test"));
            Assert.Contains(model.SourceRoots, sourceRoot =>
                sourceRoot.Ownership == ProjectSourceRootOwnership.Unknown
                && sourceRoot.IsGenerated
                && sourceRoot.Directory == Path.Combine(root, "build", "generated", "sources", "opaque"));
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleGeneratedSourceOwnershipUnknown");
            Assert.Equal(model.SourceRoots, module.SourceRoots);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_resolves_literal_custom_source_sets_and_preserves_test_ownership()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleCustomSourceSets_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"custom-source-sets\"");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                plugins { id("java") }
                sourceSets {
                    create("integrationTest") {
                        java.srcDir("src/integration/java")
                    }
                    create("contractTest") {
                        java.setSrcDirs(listOf("src/contracts/java", "src/contracts/shared"))
                    }
                    create("benchmark") {
                        java.srcDir("src/benchmark/java")
                    }
                }
                """);

            WriteJavaSource(root, "src/integration/java/example/IntegrationTest.java");
            WriteJavaSource(root, "src/contracts/java/example/ContractTest.java");
            WriteJavaSource(root, "src/contracts/shared/example/ContractFixture.java");
            WriteJavaSource(root, "src/benchmark/java/example/Benchmark.java");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);

            var integration = Assert.Single(model.SourceSets, sourceSet => sourceSet.Name == "integrationTest");
            Assert.True(integration.IsTest);
            Assert.Contains(model.SourceSets, sourceSet =>
                sourceSet.Name == "contractTest" && sourceSet.IsTest
                && sourceSet.JavaSourceDirectories.Count == 2);
            Assert.Contains(model.SourceSets, sourceSet =>
                sourceSet.Name == "benchmark" && !sourceSet.IsTest);

            Assert.Contains(model.SourceRoots, sourceRoot =>
                sourceRoot.Directory == Path.Combine(root, "src", "integration", "java")
                && sourceRoot.Ownership == Aegis.Shared.Architecture.Enums.ProjectSourceRootOwnership.Test
                && sourceRoot.Origin == Aegis.Shared.Architecture.Enums.ProjectSourceRootOrigin.Explicit);
            Assert.Contains(model.SourceRoots, sourceRoot =>
                sourceRoot.Directory == Path.Combine(root, "src", "benchmark", "java")
                && sourceRoot.Ownership == Aegis.Shared.Architecture.Enums.ProjectSourceRootOwnership.Production
                && sourceRoot.Origin == Aegis.Shared.Architecture.Enums.ProjectSourceRootOrigin.Explicit);
            Assert.True(model.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_resolves_groovy_custom_source_sets_with_nested_java_blocks_and_file_notation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleGroovySourceSets_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle"), "rootProject.name = 'groovy-source-sets'");
            File.WriteAllText(Path.Combine(root, "build.gradle"), """
                sourceSets {
                    integrationTest {
                        java {
                            srcDir file('src/integration/java')
                        }
                    }
                    benchmark {
                        java.srcDirs = ['src/benchmark/java']
                    }
                }
                """);

            WriteJavaSource(root, "src/integration/java/example/IntegrationTest.java");
            WriteJavaSource(root, "src/benchmark/java/example/Benchmark.java");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);

            Assert.Contains(model.SourceSets, sourceSet =>
                sourceSet.Name == "integrationTest" && sourceSet.IsTest);
            Assert.Contains(model.SourceSets, sourceSet =>
                sourceSet.Name == "benchmark" && !sourceSet.IsTest);
            Assert.Contains(model.SourceRoots, sourceRoot =>
                sourceRoot.Directory == Path.Combine(root, "src", "integration", "java")
                && sourceRoot.Origin == Aegis.Shared.Architecture.Enums.ProjectSourceRootOrigin.Explicit
                && sourceRoot.Ownership == Aegis.Shared.Architecture.Enums.ProjectSourceRootOwnership.Test);
            Assert.True(model.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_does_not_infer_dynamic_custom_source_set_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleDynamicSourceSet_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle"), "rootProject.name = 'dynamic-source-set'");
            File.WriteAllText(Path.Combine(root, "build.gradle"), """
                sourceSets {
                    create("generatedTest") {
                        java.srcDir(provider { generatedDirectory })
                    }
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);

            Assert.False(model.IsComplete);
            Assert.DoesNotContain(model.SourceSets, sourceSet => sourceSet.Name == "generatedTest");
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleCustomSourceSetNotStaticallyResolved");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteJavaSource(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "package example; final class Fixture {}");
    }


    [Fact]
    public void Detect_does_not_misclassify_gradle_constraints_or_dynamic_additions_as_direct_dependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AegisGradleDependencyContracts_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"),
                "rootProject.name = \"dependency-contracts\"");
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), """
                dependencies {
                    implementation("org.example:direct-api:1.0.0")
                    constraints {
                        implementation("org.example:constrained-api:2.0.0")
                    }
                    add("runtimeOnly", "org.example:dynamic-api:3.0.0")
                }
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.GradleBuildModel>(module.GradleBuildModel);

            var direct = Assert.Single(model.Dependencies);
            Assert.Equal("org.example", direct.Group);
            Assert.Equal("direct-api", direct.Artifact);
            Assert.DoesNotContain(model.Dependencies, dependency =>
                dependency.Artifact is "constrained-api" or "dynamic-api");

            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleDependencyConstraintsNotModeled");
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "GradleDependencyDeclarationNotSupported");
            Assert.False(model.IsComplete);

            var evidence = Assert.Single(context.DependencyEvidence);
            Assert.Equal("org.example:direct-api", evidence.Name);
            Assert.True(evidence.IsDirect);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

}
