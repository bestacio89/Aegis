using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JavaSemanticAnalysisOrchestratorTests
{
    [Fact]
    public async Task Analyses_only_the_source_roots_owned_by_each_java_module()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var firstRoot = Path.Combine(repository, "module-a", "src", "main", "java");
        var secondRoot = Path.Combine(repository, "module-b", "src", "main", "java");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var firstFile = Path.Combine(firstRoot, "A.java");
        var secondFile = Path.Combine(secondRoot, "B.java");
        await File.WriteAllTextAsync(firstFile, "package sample; public class A {}");
        await File.WriteAllTextAsync(secondFile, "package sample; public class B {}");

        try
        {
            var adapter = new RecordingJavaAdapter();
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);
            var context = new ProjectArchitectureContext
            {
                ProjectName = "multi-module",
                RootPath = repository,
                Language = "Java",
                Modules =
                [
                    new ProjectModuleContext
                    {
                        Name = "aggregator",
                        Path = repository,
                        BuildSystem = ArchitectureBuildSystem.Maven,
                        Languages = ["Java"]
                    },
                    CreateModule("module-a", Path.Combine(repository, "module-a"), firstRoot),
                    CreateModule("module-b", Path.Combine(repository, "module-b"), secondRoot)
                ]
            };

            await orchestrator.AnalyzeAsync(context);

            Assert.Equal(2, context.SemanticAnalysisRuns.Count);
            Assert.Equal(2, adapter.Requests.Count);
            Assert.Equal(new[] { firstFile }, adapter.Requests[0].SourceFiles);
            Assert.Equal(new[] { secondFile }, adapter.Requests[1].SourceFiles);
            Assert.DoesNotContain(adapter.Requests, request =>
                request.ProjectPath.Equals(repository, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Supplies_only_declared_production_project_source_roots_to_jdt_and_projects_resolved_cross_module_edges()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var consumerPath = Path.Combine(repository, "consumer");
        var apiPath = Path.Combine(repository, "api");
        var commonPath = Path.Combine(repository, "common");
        var testSupportPath = Path.Combine(repository, "test-support");
        var consumerRoot = Path.Combine(consumerPath, "src", "main", "java");
        var apiRoot = Path.Combine(apiPath, "src", "main", "java");
        var commonRoot = Path.Combine(commonPath, "src", "main", "java");
        var testSupportRoot = Path.Combine(testSupportPath, "src", "test", "java");
        Directory.CreateDirectory(consumerRoot);
        Directory.CreateDirectory(apiRoot);
        Directory.CreateDirectory(commonRoot);
        Directory.CreateDirectory(testSupportRoot);
        var consumerFile = Path.Combine(consumerRoot, "Consumer.java");
        var apiFile = Path.Combine(apiRoot, "PublicApi.java");
        var commonFile = Path.Combine(commonRoot, "CommonType.java");
        var testSupportFile = Path.Combine(testSupportRoot, "TestSupport.java");
        await File.WriteAllTextAsync(consumerFile, "package consumer; public class Consumer {}");
        await File.WriteAllTextAsync(apiFile, "package api; public class PublicApi {}");
        await File.WriteAllTextAsync(commonFile, "package common; public class CommonType {}");
        await File.WriteAllTextAsync(testSupportFile, "package support; public class TestSupport {}");

        try
        {
            var consumer = CreateModule("consumer", consumerPath, consumerRoot);
            var api = CreateModule("api", apiPath, apiRoot);
            var common = CreateModule("common", commonPath, commonRoot);
            var testSupport = new ProjectModuleContext
            {
                Name = "test-support",
                Path = testSupportPath,
                Languages = ["Java"],
                IsTestSupport = true,
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = testSupportRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    }
                ]
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "multi-module",
                RootPath = repository,
                Language = "Java",
                Modules = [consumer, api, common, testSupport],
                Dependencies =
                [
                    new ArchitectureDependencyContext
                    {
                        Source = "consumer",
                        SourceProjectPath = consumerPath,
                        Target = "api",
                        TargetProjectPath = apiPath,
                        DependencyType = "MavenProjectDependency",
                        Scope = "Production"
                    },
                    new ArchitectureDependencyContext
                    {
                        Source = "api",
                        SourceProjectPath = apiPath,
                        Target = "common",
                        TargetProjectPath = commonPath,
                        DependencyType = "MavenProjectDependency",
                        Scope = "Production"
                    },
                    new ArchitectureDependencyContext
                    {
                        Source = "consumer",
                        SourceProjectPath = consumerPath,
                        Target = "test-support",
                        TargetProjectPath = testSupportPath,
                        DependencyType = "MavenProjectDependency",
                        Scope = "Test"
                    }
                ]
            };
            var adapter = new SemanticGraphJavaAdapter(consumerFile);
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);

            await orchestrator.AnalyzeAsync(context);

            var consumerRequest = Assert.Single(adapter.Requests.Where(request => request.ModuleName == "consumer"));
            Assert.Contains(Path.GetFullPath(apiRoot), consumerRequest.ReferencedSourceRoots);
            Assert.Contains(Path.GetFullPath(commonRoot), consumerRequest.ReferencedSourceRoots);
            Assert.Equal(2, consumerRequest.ReferencedSourceRoots.Count);
            Assert.DoesNotContain(Path.GetFullPath(testSupportRoot), consumerRequest.ReferencedSourceRoots);

            var semanticDependency = Assert.Single(context.Dependencies.Where(dependency =>
                dependency.DependencyType == "JavaSemanticDependency"));
            Assert.Equal(Path.GetFullPath(consumerPath), semanticDependency.SourceProjectPath);
            Assert.Equal(Path.GetFullPath(apiPath), semanticDependency.TargetProjectPath);
            Assert.Equal("Production", semanticDependency.Scope);
            Assert.Equal(consumerFile, semanticDependency.File);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Projects_test_source_relationships_as_test_edges_without_losing_production_edges()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var consumerPath = Path.Combine(repository, "consumer");
        var apiPath = Path.Combine(repository, "api");
        var consumerProductionRoot = Path.Combine(consumerPath, "src", "main", "java");
        var consumerTestRoot = Path.Combine(consumerPath, "src", "test", "java");
        var apiRoot = Path.Combine(apiPath, "src", "main", "java");
        Directory.CreateDirectory(consumerProductionRoot);
        Directory.CreateDirectory(consumerTestRoot);
        Directory.CreateDirectory(apiRoot);

        var consumerProductionFile = Path.Combine(consumerProductionRoot, "Consumer.java");
        var consumerTestFile = Path.Combine(consumerTestRoot, "ConsumerTests.java");
        await File.WriteAllTextAsync(consumerProductionFile, "package consumer; public class Consumer {}");
        await File.WriteAllTextAsync(consumerTestFile, "package consumer; public class ConsumerTests {}");
        await File.WriteAllTextAsync(Path.Combine(apiRoot, "PublicApi.java"), "package api; public class PublicApi {}");

        try
        {
            var consumer = new ProjectModuleContext
            {
                Name = "consumer",
                Path = consumerPath,
                Languages = ["Java"],
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = consumerProductionRoot,
                        Ownership = ProjectSourceRootOwnership.Production,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    },
                    new ProjectSourceRoot
                    {
                        Directory = consumerTestRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    }
                ]
            };
            var api = CreateModule("api", apiPath, apiRoot);
            var context = new ProjectArchitectureContext
            {
                ProjectName = "multi-module",
                RootPath = repository,
                Language = "Java",
                Modules = [consumer, api],
                Dependencies =
                [
                    new ArchitectureDependencyContext
                    {
                        Source = "consumer",
                        SourceProjectPath = consumerPath,
                        Target = "api",
                        TargetProjectPath = apiPath,
                        DependencyType = "MavenProjectDependency",
                        Scope = "Production"
                    }
                ]
            };
            var adapter = new SemanticGraphJavaAdapter(consumerProductionFile);
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);

            await orchestrator.AnalyzeAsync(context);

            Assert.Contains(context.Dependencies, dependency =>
                dependency.DependencyType == "JavaSemanticDependency"
                && Path.GetFullPath(dependency.SourceProjectPath) == Path.GetFullPath(consumerPath)
                && Path.GetFullPath(dependency.TargetProjectPath) == Path.GetFullPath(apiPath)
                && dependency.Scope == "Production"
                && dependency.File == consumerProductionFile);
            Assert.Contains(context.Dependencies, dependency =>
                dependency.DependencyType == "JavaSemanticDependency"
                && Path.GetFullPath(dependency.SourceProjectPath) == Path.GetFullPath(consumerPath)
                && Path.GetFullPath(dependency.TargetProjectPath) == Path.GetFullPath(apiPath)
                && dependency.Scope == "Test"
                && dependency.File == consumerTestFile);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Adds_compiler_confirmed_framework_annotation_evidence()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(repository, "src", "main", "java");
        Directory.CreateDirectory(sourceRoot);
        var sourceFile = Path.Combine(sourceRoot, "Service.java");
        await File.WriteAllTextAsync(sourceFile, "package sample; public class Service {}");

        try
        {
            var adapter = new AnnotationJavaAdapter(sourceFile);
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);
            var module = new ProjectModuleContext
            {
                Name = "service",
                Path = repository,
                Languages = ["Java"],
                Framework = "Java SE",
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = sourceRoot,
                        Ownership = ProjectSourceRootOwnership.Production,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    }
                ]
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "service",
                RootPath = repository,
                Language = "Java",
                Framework = "Java SE",
                Modules = [module]
            };

            await orchestrator.AnalyzeAsync(context);

            Assert.Contains("Spring Framework", module.Frameworks);
            Assert.Contains("Spring Framework", context.DetectedFrameworks);
            var evidence = Assert.Single(module.FrameworkEvidence);
            Assert.Equal(FrameworkEvidenceKind.Web, evidence.Kind);
            Assert.False(evidence.IsTestOnly);
            Assert.Equal(SemanticEvidenceSource.Compiler, Assert.Single(evidence.Provenance).Source);
            Assert.Equal(sourceFile, evidence.Provenance[0].FilePath);
            Assert.Equal("Spring Framework", context.Framework);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Adds_versioned_production_framework_evidence_from_maven_and_gradle_coordinates()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(repository, "src", "main", "java");
        Directory.CreateDirectory(sourceRoot);
        var sourceFile = Path.Combine(sourceRoot, "Service.java");
        var pomFile = Path.Combine(repository, "pom.xml");
        await File.WriteAllTextAsync(sourceFile, "package sample; public class Service {}");
        await File.WriteAllTextAsync(pomFile, "<project />");

        try
        {
            var module = new ProjectModuleContext
            {
                Name = "service",
                Path = repository,
                Languages = ["Java"],
                Framework = "Java SE",
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = sourceRoot,
                        Ownership = ProjectSourceRootOwnership.Production,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    }
                ]
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "service",
                RootPath = repository,
                Language = "Java",
                Framework = "Java SE",
                Modules = [module],
                DependencyEvidence =
                [
                    CreateFrameworkDependency(
                        "spring-boot",
                        "org.springframework.boot:spring-boot-starter-web",
                        "Maven",
                        "3.4.1",
                        repository,
                        pomFile),
                    CreateFrameworkDependency(
                        "hibernate",
                        "org.hibernate.orm:hibernate-core",
                        "Gradle",
                        "6.6.8.Final",
                        repository,
                        Path.Combine(repository, "build.gradle.kts")),
                    CreateFrameworkDependency(
                        "test-only-spring",
                        "org.springframework.boot:spring-boot-starter-web",
                        "Maven",
                        "3.5.0",
                        repository,
                        pomFile,
                        isTestOnly: true)
                ]
            };
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [new RecordingJavaAdapter()],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);

            await orchestrator.AnalyzeAsync(context);

            var spring = Assert.Single(module.FrameworkEvidence.Where(evidence => evidence.Name == "Spring Boot"));
            Assert.Equal("3.4.1", spring.Version);
            Assert.Equal(FrameworkEvidenceKind.Web, spring.Kind);
            Assert.False(spring.IsTestOnly);
            Assert.Equal(SemanticEvidenceSource.BuildModel, Assert.Single(spring.Provenance).Source);
            Assert.Equal(pomFile, spring.Provenance[0].FilePath);

            var hibernate = Assert.Single(module.FrameworkEvidence.Where(evidence => evidence.Name == "Hibernate"));
            Assert.Equal("6.6.8.Final", hibernate.Version);
            Assert.Equal(FrameworkEvidenceKind.Persistence, hibernate.Kind);
            Assert.False(hibernate.IsTestOnly);
            Assert.Equal("Spring Boot", module.Framework);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private static ArchitectureDependencyEvidence CreateFrameworkDependency(
        string id,
        string name,
        string ecosystem,
        string version,
        string projectPath,
        string buildFile,
        bool isTestOnly = false) =>
        new()
        {
            Id = id,
            Name = name,
            Ecosystem = ecosystem,
            Kind = DependencyEvidenceKind.Package,
            DeclaredVersion = version,
            Scope = isTestOnly ? "test" : "compile",
            SourceProjectPath = projectPath,
            IsDirect = true,
            IsTestOnly = isTestOnly,
            ResolutionStatus = DependencyResolutionStatus.Declared,
            Provenance =
            [
                new SemanticEvidenceProvenance
                {
                    AnalyzerId = ecosystem == "Maven" ? "aegis.maven-model" : "aegis.gradle-build-model",
                    Source = SemanticEvidenceSource.BuildModel,
                    FilePath = buildFile,
                    Confidence = 1.0
                }
            ]
        };

    [Fact]
    public async Task Adds_test_framework_evidence_from_declared_maven_test_dependencies()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(repository, "src", "test", "java");
        Directory.CreateDirectory(sourceRoot);
        var sourceFile = Path.Combine(sourceRoot, "ServiceTests.java");
        var pomFile = Path.Combine(repository, "pom.xml");
        await File.WriteAllTextAsync(sourceFile, "package sample; public class ServiceTests {}");
        await File.WriteAllTextAsync(pomFile, "<project />");

        try
        {
            var adapter = new AnnotationJavaAdapter(sourceFile, "sample.CustomAnnotation", isTestCode: true);
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [adapter],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);
            var module = new ProjectModuleContext
            {
                Name = "service-tests",
                Path = repository,
                Languages = ["Java"],
                IsTestProject = true,
                MavenModel = new MavenProjectModel
                {
                    ProjectFile = pomFile,
                    ProjectCoordinates = new MavenProjectModel.Coordinates("sample", "service-tests", "1.0.0"),
                    Dependencies =
                    [
                        new MavenProjectModel.Dependency(
                            "org.junit.jupiter",
                            "junit-jupiter",
                            "5.10.2",
                            "test",
                            "jar",
                            null,
                            false,
                            false,
                            false,
                            [],
                            pomFile,
                            false),
                        new MavenProjectModel.Dependency(
                            "org.testcontainers",
                            "junit-jupiter",
                            "1.20.1",
                            "test",
                            "jar",
                            null,
                            false,
                            false,
                            false,
                            [],
                            pomFile,
                            false)
                    ],
                    Plugins =
                    [
                        new MavenProjectModel.Plugin("org.apache.maven.plugins", "maven-surefire-plugin", "3.2.5", false),
                        new MavenProjectModel.Plugin("org.apache.maven.plugins", "maven-failsafe-plugin", "3.2.5", false)
                    ]
                },
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = sourceRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    }
                ]
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "service-tests",
                RootPath = repository,
                Language = "Java",
                Framework = "Java SE",
                Modules = [module]
            };

            await orchestrator.AnalyzeAsync(context);

            var junit = Assert.Single(module.FrameworkEvidence.Where(evidence => evidence.Name == "JUnit 5"));
            Assert.Equal("5.10.2", junit.Version);
            Assert.Equal(FrameworkEvidenceKind.Testing, junit.Kind);
            Assert.True(junit.IsTestOnly);
            Assert.Equal(SemanticEvidenceSource.BuildModel, Assert.Single(junit.Provenance).Source);
            Assert.Contains(module.FrameworkEvidence, evidence =>
                evidence.Name == "Testcontainers" && evidence.Version == "1.20.1" && evidence.IsTestOnly);
            Assert.Contains(module.FrameworkEvidence, evidence =>
                evidence.Name == "Maven Surefire" && evidence.Version == "3.2.5" && evidence.IsTestOnly);
            Assert.Contains(module.FrameworkEvidence, evidence =>
                evidence.Name == "Maven Failsafe" && evidence.Version == "3.2.5" && evidence.IsTestOnly);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Adds_gradle_test_framework_fixture_and_integration_source_set_evidence()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mainRoot = Path.Combine(repository, "src", "main", "java");
        var testRoot = Path.Combine(repository, "src", "test", "java");
        var integrationRoot = Path.Combine(repository, "src", "integrationTest", "java");
        Directory.CreateDirectory(mainRoot);
        Directory.CreateDirectory(testRoot);
        Directory.CreateDirectory(integrationRoot);
        var buildFile = Path.Combine(repository, "build.gradle.kts");
        await File.WriteAllTextAsync(buildFile, "plugins { java }");
        await File.WriteAllTextAsync(Path.Combine(mainRoot, "Service.java"), "package sample; public class Service {}");
        await File.WriteAllTextAsync(Path.Combine(testRoot, "ServiceTests.java"), "package sample; public class ServiceTests {}");
        await File.WriteAllTextAsync(Path.Combine(integrationRoot, "ServiceIntegrationTests.java"), "package sample; public class ServiceIntegrationTests {}");

        try
        {
            var module = new ProjectModuleContext
            {
                Name = "gradle-service",
                Path = repository,
                BuildSystem = ArchitectureBuildSystem.Gradle,
                Languages = ["Java"],
                GradleBuildModel = new GradleBuildModel
                {
                    BuildFile = buildFile,
                    Dialect = "Kotlin",
                    Dependencies =
                    [
                        new GradleBuildModel.Dependency(
                            "testImplementation", "org.junit.jupiter", "junit-jupiter", "5.10.2",
                            null, null, false, false, false, "testImplementation(...)"),
                        new GradleBuildModel.Dependency(
                            "testImplementation", "org.testcontainers", "junit-jupiter", "1.20.1",
                            null, null, false, false, false, "testImplementation(...)")
                    ],
                    Plugins =
                    [
                        new GradleBuildModel.Plugin("java-test-fixtures", null, true)
                    ],
                    SourceSets =
                    [
                        new GradleBuildModel.SourceSet("main", [mainRoot], false),
                        new GradleBuildModel.SourceSet("test", [testRoot], true),
                        new GradleBuildModel.SourceSet("integrationTest", [integrationRoot], true)
                    ]
                },
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = mainRoot,
                        Ownership = ProjectSourceRootOwnership.Production,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    },
                    new ProjectSourceRoot
                    {
                        Directory = testRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    },
                    new ProjectSourceRoot
                    {
                        Directory = integrationRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    }
                ]
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "gradle-service",
                RootPath = repository,
                Language = "Java",
                Framework = "Java SE",
                Modules = [module]
            };
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [new RecordingJavaAdapter()],
                new EmptyClasspathResolver(),
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);

            await orchestrator.AnalyzeAsync(context);

            var evidenceNames = module.FrameworkEvidence.Select(evidence => evidence.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("JUnit 5", evidenceNames);
            Assert.Contains("Testcontainers", evidenceNames);
            Assert.Contains("Gradle Test Fixtures", evidenceNames);
            Assert.Contains("Gradle Test", evidenceNames);
            Assert.Contains("Integration Testing", evidenceNames);
            Assert.All(module.FrameworkEvidence, evidence =>
            {
                Assert.Equal(FrameworkEvidenceKind.Testing, evidence.Kind);
                Assert.True(evidence.IsTestOnly);
                Assert.Equal(SemanticEvidenceSource.BuildModel, Assert.Single(evidence.Provenance).Source);
            });
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Uses_separate_production_and_test_classpaths_for_owned_source_roots()
    {
        var repository = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var servicePath = Path.Combine(repository, "service");
        var testSupportPath = Path.Combine(repository, "test-support");
        var productionRoot = Path.Combine(servicePath, "src", "main", "java");
        var testRoot = Path.Combine(servicePath, "src", "test", "java");
        var testSupportRoot = Path.Combine(testSupportPath, "src", "test", "java");
        Directory.CreateDirectory(productionRoot);
        Directory.CreateDirectory(testRoot);
        Directory.CreateDirectory(testSupportRoot);

        var productionFile = Path.Combine(productionRoot, "Service.java");
        var testFile = Path.Combine(testRoot, "ServiceTests.java");
        await File.WriteAllTextAsync(productionFile, "package sample; public class Service {}");
        await File.WriteAllTextAsync(testFile, "package sample; public class ServiceTests {}");

        try
        {
            var service = new ProjectModuleContext
            {
                Name = "service",
                Path = servicePath,
                Languages = ["Java"],
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = productionRoot,
                        Ownership = ProjectSourceRootOwnership.Production,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    },
                    new ProjectSourceRoot
                    {
                        Directory = testRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    }
                ]
            };
            var testSupport = new ProjectModuleContext
            {
                Name = "test-support",
                Path = testSupportPath,
                IsTestSupport = true,
                SourceRoots =
                [
                    new ProjectSourceRoot
                    {
                        Directory = testSupportRoot,
                        Ownership = ProjectSourceRootOwnership.Test,
                        Origin = ProjectSourceRootOrigin.Explicit,
                        ContainsJavaSources = true
                    }
                ]
            };
            var context = new ProjectArchitectureContext
            {
                ProjectName = "service",
                RootPath = repository,
                Language = "Java",
                Modules = [service, testSupport],
                Dependencies =
                [
                    new ArchitectureDependencyContext
                    {
                        Source = "service",
                        SourceProjectPath = servicePath,
                        Target = "test-support",
                        TargetProjectPath = testSupportPath,
                        DependencyType = "MavenProjectDependency",
                        Scope = "Test"
                    }
                ]
            };
            var adapter = new RecordingJavaAdapter();
            var classpathResolver = new RecordingClasspathResolver();
            var orchestrator = new JavaSemanticAnalysisOrchestrator(
                [adapter],
                classpathResolver,
                NullLogger<JavaSemanticAnalysisOrchestrator>.Instance);

            await orchestrator.AnalyzeAsync(context);

            var serviceRequests = adapter.Requests
                .Where(request => Path.GetFullPath(request.ProjectPath) == Path.GetFullPath(servicePath))
                .ToArray();
            Assert.Equal(2, serviceRequests.Length);

            var productionRequest = Assert.Single(serviceRequests.Where(request =>
                request.SourceFiles.Contains(productionFile)));
            var testRequest = Assert.Single(serviceRequests.Where(request =>
                request.SourceFiles.Contains(testFile)));

            Assert.False(productionRequest.IncludeTestCode);
            Assert.True(testRequest.IncludeTestCode);
            Assert.Empty(productionRequest.TestSourceRoots);
            Assert.Equal(new[] { Path.GetFullPath(testRoot) }, testRequest.TestSourceRoots);
            Assert.Equal(new[] { Path.GetFullPath(productionRoot) }, productionRequest.SourceRoots);
            Assert.Equal(new[] { Path.GetFullPath(testRoot) }, testRequest.SourceRoots);
            Assert.DoesNotContain(Path.GetFullPath(testSupportRoot), productionRequest.ReferencedSourceRoots);
            Assert.Contains(Path.GetFullPath(testSupportRoot), testRequest.ReferencedSourceRoots);
            Assert.Contains("production-classpath.jar", productionRequest.ClasspathEntries);
            Assert.Contains("test-classpath.jar", testRequest.ClasspathEntries);
            Assert.Contains(classpathResolver.Calls, call =>
                Path.GetFullPath(call.ProjectPath) == Path.GetFullPath(servicePath)
                && !call.IncludeTestCode);
            Assert.Contains(classpathResolver.Calls, call =>
                Path.GetFullPath(call.ProjectPath) == Path.GetFullPath(servicePath)
                && call.IncludeTestCode);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private static ProjectModuleContext CreateModule(string name, string path, string sourceRoot) =>
        new()
        {
            Name = name,
            Path = path,
            BuildSystem = ArchitectureBuildSystem.Maven,
            Languages = ["Java"],
            SourceRoots =
            [
                new ProjectSourceRoot
                {
                    Directory = sourceRoot,
                    Ownership = ProjectSourceRootOwnership.Production,
                    Origin = ProjectSourceRootOrigin.Explicit,
                    ContainsJavaSources = true
                }
            ]
        };

    private sealed class RecordingJavaAdapter : ISemanticAnalysisAdapter
    {
        public List<SemanticAnalysisRequest> Requests { get; } = [];

        public string AdapterId => "fixture-java";
        public string Language => "Java";
        public IReadOnlyCollection<string> SupportedCapabilities => ["symbols"];

        public bool CanAnalyze(SemanticAnalysisRequest request) =>
            request.Language.Equals("Java", StringComparison.OrdinalIgnoreCase);

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = "Java",
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = request.SourceFiles.Count
            });
        }
    }

    private sealed class SemanticGraphJavaAdapter : ISemanticAnalysisAdapter
    {
        private readonly string _consumerFile;

        public SemanticGraphJavaAdapter(string consumerFile)
        {
            _consumerFile = consumerFile;
        }

        public List<SemanticAnalysisRequest> Requests { get; } = [];

        public string AdapterId => "eclipse-jdt";
        public string Language => "Java";
        public IReadOnlyCollection<string> SupportedCapabilities => ["symbols", "relationships"];

        public bool CanAnalyze(SemanticAnalysisRequest request) => true;

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var isApi = request.ModuleName == "api";
            var isConsumer = request.ModuleName == "consumer";
            var run = new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = "Java",
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = request.SourceFiles.Count,
                Symbols = isApi
                    ?
                    [
                        new SemanticSymbol
                        {
                            Id = "api-public-type",
                            Name = "PublicApi",
                            QualifiedName = "api.PublicApi",
                            ProjectPath = request.ProjectPath,
                            Language = "Java",
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ]
                    :
                    [
                        new SemanticSymbol
                        {
                            Id = request.ModuleName + "-type",
                            Name = request.ModuleName,
                            QualifiedName = request.ModuleName + ".Consumer",
                            ProjectPath = request.ProjectPath,
                            Language = "Java",
                            Origin = SemanticSymbolOrigin.Source
                        }
                    ],
                Relationships = !isConsumer
                    ? []
                    :
                    [
                        new SemanticRelationship
                        {
                            Id = "consumer-uses-api",
                            SourceSymbolId = request.ModuleName + "-type",
                            TargetSymbolId = "api-public-type",
                            Kind = SemanticRelationshipKind.References,
                            ResolutionStatus = SemanticResolutionStatus.Resolved,
                            IsTestCode = request.IncludeTestCode,
                            Provenance =
                            [
                                new SemanticEvidenceProvenance
                                {
                                    Source = SemanticEvidenceSource.Compiler,
                                    FilePath = request.SourceFiles.FirstOrDefault() ?? _consumerFile,
                                    StartLine = 1,
                                    StartColumn = 1,
                                    Confidence = 1.0
                                }
                            ]
                        }
                    ]
            };
            return ValueTask.FromResult(run);
        }
    }

    private sealed class AnnotationJavaAdapter : ISemanticAnalysisAdapter
    {
        private readonly string _sourceFile;
        private readonly string _annotationName;
        private readonly bool _isTestCode;

        public AnnotationJavaAdapter(
            string sourceFile,
            string annotationName = "org.springframework.web.bind.annotation.RestController",
            bool isTestCode = false)
        {
            _sourceFile = sourceFile;
            _annotationName = annotationName;
            _isTestCode = isTestCode;
        }

        public string AdapterId => "eclipse-jdt";
        public string Language => "Java";
        public IReadOnlyCollection<string> SupportedCapabilities => ["symbols", "annotations"];

        public bool CanAnalyze(SemanticAnalysisRequest request) => true;

        public ValueTask<SemanticAnalysisRun> AnalyzeAsync(
            SemanticAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            var run = new SemanticAnalysisRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Language = "Java",
                EngineId = AdapterId,
                ProjectPath = request.ProjectPath,
                Status = SemanticAnalysisStatus.Completed,
                AnalyzedFileCount = 1,
                Symbols =
                [
                    new SemanticSymbol { Id = "source", QualifiedName = "sample.Service" },
                    new SemanticSymbol { Id = "annotation", QualifiedName = _annotationName }
                ],
                Relationships =
                [
                    new SemanticRelationship
                    {
                        Id = "annotation-use",
                        SourceSymbolId = "source",
                        TargetSymbolId = "annotation",
                        Kind = SemanticRelationshipKind.UsesAnnotation,
                        ResolutionStatus = SemanticResolutionStatus.Resolved,
                        IsTestCode = _isTestCode,
                        Provenance =
                        [
                            new SemanticEvidenceProvenance
                            {
                                Source = SemanticEvidenceSource.Compiler,
                                FilePath = _sourceFile,
                                StartLine = 4,
                                StartColumn = 1,
                                Confidence = 1.0
                            }
                        ]
                    }
                ]
            };
            return ValueTask.FromResult(run);
        }
    }

    private sealed class RecordingClasspathResolver : IJavaClasspathResolver
    {
        public List<(string ProjectPath, bool IncludeTestCode)> Calls { get; } = [];

        public JavaClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode)
        {
            Calls.Add((module.Path, includeTestCode));
            return new JavaClasspathResolution(
                [includeTestCode ? "test-classpath.jar" : "production-classpath.jar"],
                [],
                true,
                true);
        }
    }

    private sealed class EmptyClasspathResolver : IJavaClasspathResolver
    {
        public JavaClasspathResolution Resolve(
            ProjectModuleContext module,
            IReadOnlyCollection<ProjectModuleContext> projectModules,
            bool includeTestCode) =>
            new(Array.Empty<string>(), Array.Empty<string>(), false, true);
    }
}
