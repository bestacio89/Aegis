using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Enums;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class MavenProjectModelTests
{
    [Fact]
    public void Detect_resolves_kotlin_maven_compiler_options_and_mixed_source_roots()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>kotlin-service</artifactId>
                  <version>1.0.0</version>
                  <properties>
                    <kotlin.version>2.4.21</kotlin.version>
                  </properties>
                  <build>
                    <sourceDirectory>src/main/shared</sourceDirectory>
                    <plugins>
                      <plugin>
                        <groupId>org.jetbrains.kotlin</groupId>
                        <artifactId>kotlin-maven-plugin</artifactId>
                        <version>${kotlin.version}</version>
                        <executions>
                          <execution>
                            <id>compile</id>
                            <goals><goal>compile</goal></goals>
                          </execution>
                          <execution>
                            <id>test-compile</id>
                            <goals><goal>test-compile</goal></goals>
                          </execution>
                        </executions>
                        <configuration>
                          <languageVersion>2.4</languageVersion>
                          <apiVersion>2.4</apiVersion>
                          <jvmTarget>21</jvmTarget>
                          <sourceDirs>
                            <sourceDir>src/main/shared</sourceDir>
                          </sourceDirs>
                          <testSourceDirs>
                            <testSourceDir>src/test/customKotlin</testSourceDir>
                          </testSourceDirs>
                        </configuration>
                      </plugin>
                    </plugins>
                  </build>
                </project>
                """);

            var sharedRoot = Path.Combine(root, "src", "main", "shared");
            var testKotlinRoot = Path.Combine(root, "src", "test", "customKotlin");
            var generatedMainRoot = Path.Combine(root, "target", "generated-sources", "kotlin");
            var generatedTestRoot = Path.Combine(root, "target", "generated-test-sources", "kotlin");
            Directory.CreateDirectory(Path.Combine(sharedRoot, "sample"));
            Directory.CreateDirectory(Path.Combine(testKotlinRoot, "sample"));
            Directory.CreateDirectory(Path.Combine(generatedMainRoot, "sample"));
            Directory.CreateDirectory(Path.Combine(generatedTestRoot, "sample"));
            File.WriteAllText(Path.Combine(sharedRoot, "sample", "JavaContract.java"),
                "package sample; public interface JavaContract {}");
            File.WriteAllText(Path.Combine(sharedRoot, "sample", "KotlinService.kt"),
                "package sample\nclass KotlinService : JavaContract");
            File.WriteAllText(Path.Combine(testKotlinRoot, "sample", "KotlinServiceTest.kt"),
                "package sample\nclass KotlinServiceTest");
            File.WriteAllText(Path.Combine(generatedMainRoot, "sample", "GeneratedMain.kt"),
                "package sample\nclass GeneratedMain");
            File.WriteAllText(Path.Combine(generatedTestRoot, "sample", "GeneratedTest.kt"),
                "package sample\nclass GeneratedTest");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(module.MavenModel);

            Assert.Equal("2.4.21", model.KotlinPluginVersion);
            Assert.Equal("2.4", model.KotlinLanguageVersion);
            Assert.Equal("2.4", model.KotlinApiVersion);
            Assert.Equal("21", model.KotlinJvmTarget);

            var mixedRoot = Assert.Single(model.SourceRoots, sourceRoot => sourceRoot.Directory == sharedRoot);
            Assert.True(mixedRoot.ContainsJavaSources);
            Assert.True(mixedRoot.ContainsKotlinSources);
            Assert.True(mixedRoot.ContainsLanguage("Java"));
            Assert.True(mixedRoot.ContainsLanguage("Kotlin"));
            Assert.Equal(ProjectSourceRootOwnership.Production, mixedRoot.Ownership);

            var testRoot = Assert.Single(model.SourceRoots, sourceRoot => sourceRoot.Directory == testKotlinRoot);
            Assert.True(testRoot.ContainsKotlinSources);
            Assert.Equal(ProjectSourceRootOwnership.Test, testRoot.Ownership);

            var generatedMainSourceRoot = Assert.Single(model.SourceRoots,
                sourceRoot => sourceRoot.Directory == generatedMainRoot);
            Assert.True(generatedMainSourceRoot.IsGenerated);
            Assert.True(generatedMainSourceRoot.ContainsKotlinSources);
            Assert.Equal(ProjectSourceRootOwnership.Production, generatedMainSourceRoot.Ownership);

            var generatedTestSourceRoot = Assert.Single(model.SourceRoots,
                sourceRoot => sourceRoot.Directory == generatedTestRoot);
            Assert.True(generatedTestSourceRoot.IsGenerated);
            Assert.True(generatedTestSourceRoot.ContainsKotlinSources);
            Assert.Equal(ProjectSourceRootOwnership.Test, generatedTestSourceRoot.Ownership);
            Assert.True(model.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_resolves_kotlin_compiler_properties_and_maven_release_when_extensions_are_enabled()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>kotlin-properties</artifactId>
                  <version>1.0.0</version>
                  <properties>
                    <kotlin.version>2.4.21</kotlin.version>
                    <kotlin.compiler.languageVersion>2.3</kotlin.compiler.languageVersion>
                    <kotlin.compiler.apiVersion>2.2</kotlin.compiler.apiVersion>
                    <maven.compiler.release>17</maven.compiler.release>
                  </properties>
                  <build>
                    <plugins>
                      <plugin>
                        <groupId>org.jetbrains.kotlin</groupId>
                        <artifactId>kotlin-maven-plugin</artifactId>
                        <version>${kotlin.version}</version>
                        <extensions>true</extensions>
                      </plugin>
                    </plugins>
                  </build>
                </project>
                """);

            var sourceRoot = Path.Combine(root, "src", "main", "kotlin");
            Directory.CreateDirectory(Path.Combine(sourceRoot, "sample"));
            File.WriteAllText(Path.Combine(sourceRoot, "sample", "Service.kt"),
                "package sample\nclass Service");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(module.MavenModel);

            Assert.Equal("2.4.21", model.KotlinPluginVersion);
            Assert.Equal("2.3", model.KotlinLanguageVersion);
            Assert.Equal("2.2", model.KotlinApiVersion);
            Assert.Equal("17", model.KotlinJvmTarget);
            Assert.Equal("17", model.KotlinJdkRelease);
            Assert.True(model.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_inherits_kotlin_maven_plugin_source_roots_and_compiler_options_from_parent_pom()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var parentDirectory = root;
            var childDirectory = Path.Combine(root, "child");
            Directory.CreateDirectory(childDirectory);

            File.WriteAllText(Path.Combine(parentDirectory, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>kotlin-parent</artifactId>
                  <version>1.0.0</version>
                  <packaging>pom</packaging>
                  <modules><module>child</module></modules>
                  <properties><kotlin.version>2.4.21</kotlin.version></properties>
                  <build>
                    <plugins>
                      <plugin>
                        <groupId>org.jetbrains.kotlin</groupId>
                        <artifactId>kotlin-maven-plugin</artifactId>
                        <version>${kotlin.version}</version>
                        <configuration>
                          <languageVersion>2.3</languageVersion>
                          <apiVersion>2.2</apiVersion>
                          <jvmTarget>17</jvmTarget>
                          <sourceDirs>
                            <sourceDir>src/parent-config-kotlin</sourceDir>
                          </sourceDirs>
                          <testSourceDirs>
                            <testSourceDir>src/parent-config-test-kotlin</testSourceDir>
                          </testSourceDirs>
                        </configuration>
                      </plugin>
                    </plugins>
                  </build>
                </project>
                """);

            File.WriteAllText(Path.Combine(childDirectory, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <parent>
                    <groupId>org.example</groupId>
                    <artifactId>kotlin-parent</artifactId>
                    <version>1.0.0</version>
                    <relativePath>../pom.xml</relativePath>
                  </parent>
                  <artifactId>kotlin-child</artifactId>
                </project>
                """);

            var mainRoot = Path.Combine(childDirectory, "src", "parent-config-kotlin");
            var testRoot = Path.Combine(childDirectory, "src", "parent-config-test-kotlin");
            Directory.CreateDirectory(mainRoot);
            Directory.CreateDirectory(testRoot);
            File.WriteAllText(Path.Combine(mainRoot, "ChildService.kt"), "class ChildService");
            File.WriteAllText(Path.Combine(testRoot, "ChildServiceTest.kt"), "class ChildServiceTest");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var childModule = Assert.Single(context.Modules, module => module.Path == childDirectory);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(childModule.MavenModel);

            Assert.Equal("2.4.21", model.KotlinPluginVersion);
            Assert.Equal("2.3", model.KotlinLanguageVersion);
            Assert.Equal("2.2", model.KotlinApiVersion);
            Assert.Equal("17", model.KotlinJvmTarget);
            Assert.Contains("src/parent-config-kotlin", model.KotlinMainSourceDirectoryExpressions);
            Assert.Contains("src/parent-config-test-kotlin", model.KotlinTestSourceDirectoryExpressions);
            Assert.Equal(
                ProjectSourceRootOwnership.Production,
                Assert.Single(model.SourceRoots, sourceRoot => sourceRoot.Directory == mainRoot).Ownership);
            Assert.Equal(
                ProjectSourceRootOwnership.Test,
                Assert.Single(model.SourceRoots, sourceRoot => sourceRoot.Directory == testRoot).Ownership);
            Assert.True(model.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_resolves_local_parent_coordinates_properties_managed_versions_and_scopes()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var parentDirectory = Path.Combine(root, "parent");
            var serviceDirectory = Path.Combine(root, "service");
            Directory.CreateDirectory(parentDirectory);
            Directory.CreateDirectory(serviceDirectory);

            File.WriteAllText(Path.Combine(parentDirectory, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>fixture-parent</artifactId>
                  <version>2.4.0</version>
                  <packaging>pom</packaging>
                  <properties>
                    <revision>2.4.0</revision>
                    <junit.version>5.12.2</junit.version>
                    <spring.version>6.2.8</spring.version>
                  </properties>
                  <modules><module>../service</module></modules>
                  <dependencies><dependency><groupId>org.example</groupId><artifactId>parent-api</artifactId><version>1.0.0</version></dependency></dependencies>
                  <build>
                    <plugins>
                      <plugin>
                        <groupId>org.apache.maven.plugins</groupId>
                        <artifactId>maven-compiler-plugin</artifactId>
                        <configuration>
                          <annotationProcessorPaths>
                            <path>
                              <groupId>org.example</groupId>
                              <artifactId>parent-processor</artifactId>
                              <version>1.0.0</version>
                            </path>
                          </annotationProcessorPaths>
                        </configuration>
                      </plugin>
                    </plugins>
                  </build>
                  <dependencyManagement>
                    <dependencies>
                      <dependency>
                        <groupId>org.junit.jupiter</groupId>
                        <artifactId>junit-jupiter</artifactId>
                        <version>${junit.version}</version>
                      </dependency>
                      <dependency>
                        <groupId>org.springframework</groupId>
                        <artifactId>spring-context</artifactId>
                        <version>${spring.version}</version>
                      </dependency>
                    </dependencies>
                  </dependencyManagement>
                </project>
                """);

            var bomDirectory = Path.Combine(root, "bom");
            Directory.CreateDirectory(bomDirectory);
            File.WriteAllText(Path.Combine(bomDirectory, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example.build</groupId>
                  <artifactId>fixture-bom</artifactId>
                  <version>1.0.0</version>
                  <packaging>pom</packaging>
                  <dependencyManagement>
                    <dependencies>
                      <dependency>
                        <groupId>org.example</groupId>
                        <artifactId>bom-managed</artifactId>
                        <version>7.8.9</version>
                      </dependency>
                    </dependencies>
                  </dependencyManagement>
                </project>
                """);

            File.WriteAllText(Path.Combine(serviceDirectory, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <parent>
                    <groupId>org.example</groupId>
                    <artifactId>fixture-parent</artifactId>
                    <version>2.4.0</version>
                    <relativePath>../parent/pom.xml</relativePath>
                  </parent>
                  <artifactId>fixture-service</artifactId>
                  <properties><service.revision>${project.version}</service.revision></properties>
                  <dependencyManagement>
                    <dependencies>
                      <dependency>
                        <groupId>org.example.build</groupId>
                        <artifactId>fixture-bom</artifactId>
                        <version>1.0.0</version>
                        <type>pom</type>
                        <scope>import</scope>
                      </dependency>
                    </dependencies>
                  </dependencyManagement>
                  <dependencies>
                    <dependency>
                      <groupId>org.example</groupId>
                      <artifactId>bom-managed</artifactId>
                    </dependency>
                    <dependency>
                      <groupId>org.springframework</groupId>
                      <artifactId>spring-context</artifactId>
                    </dependency>
                    <dependency>
                      <groupId>org.junit.jupiter</groupId>
                      <artifactId>junit-jupiter</artifactId>
                      <scope>test</scope>
                    </dependency>
                    <dependency>
                      <groupId>org.example</groupId>
                      <artifactId>internal-api</artifactId>
                      <version>${service.revision}</version>
                      <scope>runtime</scope>
                      <exclusions>
                        <exclusion><groupId>org.example</groupId><artifactId>excluded</artifactId></exclusion>
                      </exclusions>
                    </dependency>
                  </dependencies>
                  <build>
                    <pluginManagement>
                      <plugins>
                        <plugin>
                          <groupId>org.example.build</groupId>
                          <artifactId>managed-codegen-plugin</artifactId>
                          <version>2.0.0</version>
                        </plugin>
                      </plugins>
                    </pluginManagement>
                    <plugins>
                      <plugin>
                        <groupId>org.apache.maven.plugins</groupId>
                        <artifactId>maven-compiler-plugin</artifactId>
                        <version>3.13.0</version>
                        <configuration>
                          <annotationProcessorPaths>
                            <path>
                              <groupId>org.mapstruct</groupId>
                              <artifactId>mapstruct-processor</artifactId>
                              <version>1.6.3</version>
                            </path>
                          </annotationProcessorPaths>
                        </configuration>
                      </plugin>
                    </plugins>
                  </build>
                </project>
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var parentModule = Assert.Single(context.Modules, candidate => candidate.Name == "fixture-parent");
            var parentModel = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(parentModule.MavenModel);
            Assert.Contains(Path.Combine(serviceDirectory, "pom.xml"), parentModel.ReactorModuleFiles);

            var module = Assert.Single(context.Modules, candidate => candidate.Name == "fixture-service");
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(module.MavenModel);

            Assert.Equal("org.example", model.ProjectCoordinates.GroupId);
            Assert.Equal("fixture-service", model.ProjectCoordinates.ArtifactId);
            Assert.Equal("2.4.0", model.ProjectCoordinates.Version);
            Assert.Equal("org.example", model.ParentCoordinates?.GroupId);
            Assert.Equal("fixture-parent", model.ParentCoordinates?.ArtifactId);
            Assert.Equal(Path.Combine(parentDirectory, "pom.xml"), model.ParentProjectFile);
            Assert.Equal("2.4.0", model.EffectiveProperties["service.revision"]);

            var inheritedDependency = Assert.Single(model.Dependencies, dependency => dependency.ArtifactId == "parent-api");
            Assert.True(inheritedDependency.IsInherited);
            Assert.Contains(context.DependencyEvidence, item =>
                item.Name == "org.example:parent-api"
                && !item.IsDirect
                && item.Provenance.Any(provenance => provenance.FilePath == Path.Combine(parentDirectory, "pom.xml")));

            var bomManaged = Assert.Single(model.Dependencies, dependency =>
                dependency.ArtifactId == "bom-managed");
            Assert.Equal("7.8.9", bomManaged.Version);
            Assert.True(bomManaged.IsVersionManaged);
            Assert.Contains(model.ManagedDependencies, dependency =>
                dependency.ArtifactId == "bom-managed"
                && dependency.SourceProjectFile == Path.Combine(bomDirectory, "pom.xml"));

            var spring = Assert.Single(model.Dependencies, dependency =>
                dependency.ArtifactId == "spring-context");
            Assert.Equal("6.2.8", spring.Version);
            Assert.True(spring.IsVersionManaged);
            Assert.Equal("compile", spring.Scope);

            var junit = Assert.Single(model.Dependencies, dependency =>
                dependency.ArtifactId == "junit-jupiter");
            Assert.Equal("5.12.2", junit.Version);
            Assert.True(junit.IsVersionManaged);
            Assert.Equal("test", junit.Scope);

            var internalApi = Assert.Single(model.Dependencies, dependency =>
                dependency.ArtifactId == "internal-api");
            Assert.Equal("2.4.0", internalApi.Version);
            Assert.Equal("runtime", internalApi.Scope);
            Assert.Single(internalApi.Exclusions);

            Assert.Contains(model.Plugins, plugin =>
                plugin.ArtifactId == "maven-compiler-plugin" && plugin.Version == "3.13.0");
            Assert.DoesNotContain(model.Plugins, plugin =>
                plugin.ArtifactId == "managed-codegen-plugin");
            Assert.Contains(model.ManagedPlugins, plugin =>
                plugin.ArtifactId == "managed-codegen-plugin" && plugin.Version == "2.0.0");

            var processor = Assert.Single(model.AnnotationProcessorPaths,
                dependency => dependency.ArtifactId == "mapstruct-processor");
            Assert.Equal("mapstruct-processor", processor.ArtifactId);
            Assert.Equal("1.6.3", processor.Version);
            Assert.Contains(context.DependencyEvidence, item =>
                item.Name == "org.mapstruct:mapstruct-processor"
                && item.Scope == "annotationProcessor"
                && item.IsDirect
                && item.ResolutionStatus == DependencyResolutionStatus.Declared);
            Assert.Contains(context.DependencyEvidence, item =>
                item.Name == "org.example:parent-processor"
                && item.Scope == "annotationProcessor"
                && !item.IsDirect
                && item.Provenance.Any(provenance =>
                    provenance.FilePath == Path.Combine(parentDirectory, "pom.xml")));

            var evidence = context.DependencyEvidence.Where(item =>
                item.SourceProjectPath == module.Path).ToArray();
            Assert.Contains(evidence, item =>
                item.Name == "org.springframework:spring-context"
                && item.DeclaredVersion == "6.2.8"
                && item.ResolvedVersion is null
                && item.ResolutionStatus == DependencyResolutionStatus.Declared);
            Assert.Contains(evidence, item =>
                item.Name == "org.junit.jupiter:junit-jupiter"
                && item.IsTestOnly
                && item.Scope == "test");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_reports_unresolved_parent_and_bom_without_inventing_effective_versions()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <parent>
                    <groupId>org.external</groupId>
                    <artifactId>missing-parent</artifactId>
                    <version>9.0.0</version>
                    <relativePath>../not-present/pom.xml</relativePath>
                  </parent>
                  <artifactId>fixture</artifactId>
                  <dependencyManagement>
                    <dependencies>
                      <dependency>
                        <groupId>org.external</groupId>
                        <artifactId>missing-bom</artifactId>
                        <version>1.0.0</version>
                        <type>pom</type>
                        <scope>import</scope>
                      </dependency>
                    </dependencies>
                  </dependencyManagement>
                  <modules><module>missing-module</module></modules>
                  <profiles><profile><id>integration-only</id></profile></profiles>
                  <dependencies>
                    <dependency>
                      <groupId>org.external</groupId>
                      <artifactId>managed-by-missing-bom</artifactId>
                    </dependency>
                    <dependency>
                      <groupId>org.external</groupId>
                      <artifactId>unresolved-property-version</artifactId>
                      <version>${missing.version}</version>
                    </dependency>
                  </dependencies>
                </project>
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(module.MavenModel);

            Assert.False(model.IsComplete);
            Assert.Contains(model.Diagnostics, diagnostic => diagnostic.Code == "MavenParentNotResolved");
            Assert.Equal("ExternalDeclared", model.ParentResolutionStatus);
            Assert.Contains(model.BomImports, bom =>
                bom.Coordinates.ArtifactId == "missing-bom"
                && bom.ResolutionStatus == "ExternalOrUnavailable");
            Assert.Contains(model.Diagnostics, diagnostic => diagnostic.Code == "MavenBomNotResolved");
            Assert.Contains(model.Diagnostics, diagnostic => diagnostic.Code == "MavenProfilesNotEvaluated");
            Assert.Contains(model.Diagnostics, diagnostic => diagnostic.Code == "MavenReactorModuleNotFound");
            var dependency = Assert.Single(model.Dependencies, item =>
                item.ArtifactId == "managed-by-missing-bom");
            Assert.Null(dependency.Version);
            Assert.Contains(context.DependencyEvidence, item =>
                item.Name == "org.external:managed-by-missing-bom"
                && item.ResolutionStatus == DependencyResolutionStatus.PartiallyResolved);
            Assert.Contains(context.DependencyEvidence, item =>
                item.Name == "org.external:unresolved-property-version"
                && item.ResolutionStatus == DependencyResolutionStatus.PartiallyResolved);
            Assert.Contains(context.DependencyEvidence, item =>
                item.Name == "org.external:missing-bom"
                && item.Kind == DependencyEvidenceKind.Platform
                && item.Scope == "import");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_preserves_maven_dependency_scope_and_declaration_metadata_matrix()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>scope-matrix</artifactId>
                  <version>1.0.0</version>
                  <dependencies>
                    <dependency>
                      <groupId>org.example</groupId>
                      <artifactId>compile-api</artifactId>
                      <version>1.0.0</version>
                    </dependency>
                    <dependency>
                      <groupId>org.example</groupId>
                      <artifactId>provided-api</artifactId>
                      <version>2.0.0</version>
                      <scope>provided</scope>
                    </dependency>
                    <dependency>
                      <groupId>org.example</groupId>
                      <artifactId>system-api</artifactId>
                      <version>3.0.0</version>
                      <scope>system</scope>
                      <systemPath>${project.basedir}/lib/system-api.jar</systemPath>
                    </dependency>
                    <dependency>
                      <groupId>org.example</groupId>
                      <artifactId>optional-test-support</artifactId>
                      <version>4.0.0</version>
                      <scope>test</scope>
                      <optional>true</optional>
                      <type>test-jar</type>
                      <classifier>tests</classifier>
                    </dependency>
                  </dependencies>
                </project>
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(module.MavenModel);

            Assert.Equal("compile", Assert.Single(model.Dependencies,
                dependency => dependency.ArtifactId == "compile-api").Scope);
            Assert.Equal("provided", Assert.Single(model.Dependencies,
                dependency => dependency.ArtifactId == "provided-api").Scope);
            Assert.Equal("system", Assert.Single(model.Dependencies,
                dependency => dependency.ArtifactId == "system-api").Scope);

            var optional = Assert.Single(model.Dependencies,
                dependency => dependency.ArtifactId == "optional-test-support");
            Assert.Equal("test", optional.Scope);
            Assert.True(optional.Optional);
            Assert.Equal("test-jar", optional.Type);
            Assert.Equal("tests", optional.Classifier);

            Assert.Contains(context.DependencyEvidence, evidence =>
                evidence.Name == "org.example:provided-api"
                && evidence.Scope == "provided" && !evidence.IsTestOnly);
            Assert.Contains(context.DependencyEvidence, evidence =>
                evidence.Name == "org.example:system-api"
                && evidence.Scope == "system" && !evidence.IsTestOnly);
            Assert.Contains(context.DependencyEvidence, evidence =>
                evidence.Name == "org.example:optional-test-support"
                && evidence.Scope == "test" && evidence.IsTestOnly);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_classifies_conventional_and_generated_maven_source_roots_by_ownership()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>generated-source-fixture</artifactId>
                  <version>1.0.0</version>
                </project>
                """);

            WriteJavaSource(root, "src/main/java/org/example/Production.java");
            WriteJavaSource(root, "src/test/java/org/example/ProductionTest.java");
            WriteJavaSource(root, "target/generated-sources/annotations/org/example/GeneratedMapper.java");
            WriteJavaSource(root, "target/generated-test-sources/test-annotations/org/example/GeneratedTestMapper.java");

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var roots = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(module.MavenModel)
                .SourceRoots;

            Assert.Contains(roots, sourceRoot =>
                sourceRoot.Ownership == ProjectSourceRootOwnership.Production
                && sourceRoot.Origin == ProjectSourceRootOrigin.Conventional
                && !sourceRoot.IsGenerated
                && sourceRoot.ContainsJavaSources
                && sourceRoot.Directory == Path.Combine(root, "src", "main", "java"));
            Assert.Contains(roots, sourceRoot =>
                sourceRoot.Ownership == ProjectSourceRootOwnership.Test
                && sourceRoot.Origin == ProjectSourceRootOrigin.Conventional
                && sourceRoot.Directory == Path.Combine(root, "src", "test", "java"));
            Assert.Contains(roots, sourceRoot =>
                sourceRoot.Ownership == ProjectSourceRootOwnership.Production
                && sourceRoot.Origin == ProjectSourceRootOrigin.GeneratedConvention
                && sourceRoot.IsGenerated
                && sourceRoot.Directory == Path.Combine(root, "target", "generated-sources", "annotations"));
            Assert.Contains(roots, sourceRoot =>
                sourceRoot.Ownership == ProjectSourceRootOwnership.Test
                && sourceRoot.Origin == ProjectSourceRootOrigin.GeneratedConvention
                && sourceRoot.IsGenerated
                && sourceRoot.Directory == Path.Combine(root, "target", "generated-test-sources", "test-annotations"));
            Assert.Equal(roots, module.SourceRoots);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_does_not_inherit_a_local_parent_whose_coordinates_do_not_match()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var localParent = Path.Combine(root, "local-parent");
            var child = Path.Combine(root, "child");
            Directory.CreateDirectory(localParent);
            Directory.CreateDirectory(child);

            File.WriteAllText(Path.Combine(localParent, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>not-the-declared-parent</artifactId>
                  <version>1.0.0</version>
                  <packaging>pom</packaging>
                  <properties><must.not.inherit>wrong-parent</must.not.inherit></properties>
                  <dependencies>
                    <dependency><groupId>org.example</groupId><artifactId>wrong-parent-dependency</artifactId><version>1.0</version></dependency>
                  </dependencies>
                </project>
                """);

            File.WriteAllText(Path.Combine(child, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <parent>
                    <groupId>org.example</groupId>
                    <artifactId>expected-parent</artifactId>
                    <version>1.0.0</version>
                    <relativePath>../local-parent/pom.xml</relativePath>
                  </parent>
                  <artifactId>child</artifactId>
                </project>
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules, candidate => candidate.Name == "child");
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(module.MavenModel);

            Assert.Null(model.ParentProjectFile);
            Assert.Equal("LocalCoordinatesMismatch", model.ParentResolutionStatus);
            Assert.False(model.EffectiveProperties.ContainsKey("must.not.inherit"));
            Assert.DoesNotContain(model.Dependencies, dependency =>
                dependency.ArtifactId == "wrong-parent-dependency");
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "MavenParentCoordinatesMismatch");
            Assert.False(model.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Detect_preserves_profile_content_and_activation_without_merging_it_into_unconditional_model()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "pom.xml"), """
                <project>
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>org.example</groupId>
                  <artifactId>profile-fixture</artifactId>
                  <version>1.0.0</version>
                  <profiles>
                    <profile>
                      <id>integration</id>
                      <activation>
                        <property><name>run.integration</name><value>true</value></property>
                      </activation>
                      <properties><integration.version>3.2.1</integration.version></properties>
                      <dependencies>
                        <dependency>
                          <groupId>org.example</groupId>
                          <artifactId>integration-support</artifactId>
                          <version>${integration.version}</version>
                          <scope>test</scope>
                        </dependency>
                      </dependencies>
                      <build>
                        <plugins>
                          <plugin>
                            <groupId>org.example.build</groupId>
                            <artifactId>integration-plugin</artifactId>
                            <version>2.0.0</version>
                          </plugin>
                        </plugins>
                      </build>
                    </profile>
                  </profiles>
                </project>
                """);

            var context = ProjectArchitectureContextDetector.Detect(root);
            var module = Assert.Single(context.Modules);
            var model = Assert.IsType<Aegis.Shared.Architecture.Models.MavenProjectModel>(module.MavenModel);
            var profile = Assert.Single(model.Profiles);

            Assert.Equal("integration", profile.Id);
            Assert.Equal("run.integration", profile.Activation.PropertyName);
            Assert.Equal("true", profile.Activation.PropertyValue);
            Assert.Equal("3.2.1", profile.Properties["integration.version"]);
            Assert.Equal("3.2.1", Assert.Single(profile.Dependencies).Version);
            Assert.Equal("integration-plugin", Assert.Single(profile.Plugins).ArtifactId);
            Assert.Empty(model.Dependencies);
            Assert.Empty(model.Plugins);
            Assert.Contains("integration", model.ProfileIds);
            Assert.Contains(model.Diagnostics, diagnostic =>
                diagnostic.Code == "MavenProfilesNotEvaluated");
            Assert.False(model.IsComplete);
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
        File.WriteAllText(path, "package org.example; final class Fixture {}");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AegisMavenModel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
