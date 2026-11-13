using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JavaFixtureCorpusTests
{
    private static readonly string FixtureRoot = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "Java");

    [Fact]
    public void Corpus_manifest_references_existing_fixture_roots_and_required_scenarios()
    {
        var manifestPath = Path.Combine(FixtureRoot, "corpus.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());

        var corpora = manifest.RootElement.GetProperty("corpora").EnumerateArray().ToArray();
        Assert.Equal(2, corpora.Length);

        var corpusIds = corpora
            .Select(corpus => corpus.GetProperty("id").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("maven-multi-module", corpusIds);
        Assert.Contains("gradle-kotlin-multi-project", corpusIds);

        foreach (var corpus in corpora)
        {
            var root = corpus.GetProperty("root").GetString();
            Assert.False(string.IsNullOrWhiteSpace(root));
            Assert.True(File.Exists(ResolveFixture(root!)), $"Missing fixture root: {root}");

            var scenarios = corpus.GetProperty("requiredEvidence")
                .EnumerateArray()
                .Select(item => item.GetString())
                .ToArray();

            Assert.NotEmpty(scenarios);
            Assert.All(scenarios, scenario => Assert.False(string.IsNullOrWhiteSpace(scenario)));
            Assert.Equal(scenarios.Length, scenarios.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void Maven_fixture_preserves_reactor_inheritance_dependency_and_source_evidence()
    {
        var rootPom = XDocument.Load(ResolveFixture("MavenMultiModule/pom.xml.fixture"));
        var root = rootPom.Root!;

        Assert.Equal("aegis-java-fixture-parent", ChildValue(root, "artifactId"));
        Assert.Equal("pom", ChildValue(root, "packaging"));
        Assert.Equal(
            new[] { "api", "service", "test-support" },
            root.Descendants().Where(element => element.Name.LocalName == "module")
                .Select(element => element.Value.Trim()).ToArray());

        var managedDependencies = root.Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .ToArray();
        Assert.Contains(managedDependencies, dependency =>
            ChildValue(dependency, "artifactId") == "junit-bom"
            && ChildValue(dependency, "scope") == "import");

        var servicePom = XDocument.Load(ResolveFixture(
            "MavenMultiModule/service/pom.xml.fixture"));
        var serviceDependencies = servicePom.Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .ToArray();

        Assert.Contains(serviceDependencies, dependency =>
            ChildValue(dependency, "artifactId") == "fixture-api");
        Assert.Contains(serviceDependencies, dependency =>
            ChildValue(dependency, "artifactId") == "missing-at-runtime"
            && ChildValue(dependency, "scope") == "runtime"
            && dependency.Descendants().Any(element =>
                element.Name.LocalName == "exclusion"
                && ChildValue(element, "artifactId") == "excluded-library"));
        Assert.Contains(serviceDependencies, dependency =>
            ChildValue(dependency, "artifactId") == "junit-jupiter"
            && ChildValue(dependency, "scope") == "test");

        Assert.Contains(servicePom.Descendants(), element =>
            element.Name.LocalName == "annotationProcessorPaths");
        Assert.True(File.Exists(ResolveFixture(
            "MavenMultiModule/service/src/main/java/org/aegis/fixture/domain/Order.java.fixture")));
        Assert.True(File.Exists(ResolveFixture(
            "MavenMultiModule/service/src/test/java/org/aegis/fixture/application/OrderServiceTest.java.fixture")));
        Assert.True(File.Exists(ResolveFixture(
            "MavenMultiModule/service/src/main/java/module-info.java.fixture")));

        Assert.True(File.Exists(ResolveFixture(
            "MavenMultiModule/service/target/generated-sources/annotations/org/aegis/fixture/generated/OrderMapperImpl.java.fixture")));

        var orderSource = File.ReadAllText(ResolveFixture(
            "MavenMultiModule/service/src/main/java/org/aegis/fixture/domain/Order.java.fixture"));
        Assert.Contains("record Order", orderSource, StringComparison.Ordinal);
        Assert.Contains("implements Entity<String>", orderSource, StringComparison.Ordinal);

        var serviceSource = File.ReadAllText(ResolveFixture(
            "MavenMultiModule/service/src/main/java/org/aegis/fixture/application/OrderService.java.fixture"));
        Assert.Contains("@Service", serviceSource, StringComparison.Ordinal);
        Assert.Contains("OrderRepository repository", serviceSource, StringComparison.Ordinal);

        var reflectionSource = File.ReadAllText(ResolveFixture(
            "MavenMultiModule/service/src/main/java/org/aegis/fixture/integration/PluginLoader.java.fixture"));
        Assert.Contains("Class.forName", reflectionSource, StringComparison.Ordinal);
        Assert.Contains("ServiceLoader.load", reflectionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Gradle_fixture_preserves_settings_catalog_toolchain_configurations_and_source_sets()
    {
        var settings = File.ReadAllText(ResolveFixture(
            "GradleKotlinMultiProject/settings.gradle.kts.fixture"));
        var build = File.ReadAllText(ResolveFixture(
            "GradleKotlinMultiProject/build.gradle.kts.fixture"));
        var serviceBuild = File.ReadAllText(ResolveFixture(
            "GradleKotlinMultiProject/service/build.gradle.kts.fixture"));
        var catalog = File.ReadAllText(ResolveFixture(
            "GradleKotlinMultiProject/gradle/libs.versions.toml.fixture"));

        Assert.Contains("includeBuild(\"build-logic\")", settings, StringComparison.Ordinal);
        Assert.Contains("include(\":api\", \":service\", \":integration-tests\")", settings, StringComparison.Ordinal);
        Assert.Contains("versionCatalogs", settings, StringComparison.Ordinal);
        Assert.Contains("JavaLanguageVersion.of(21)", build, StringComparison.Ordinal);
        Assert.Contains("configurations.create(\"contractTestImplementation\")", build, StringComparison.Ordinal);
        Assert.Contains("annotationProcessor(libs.mapstruct.processor)", serviceBuild, StringComparison.Ordinal);
        Assert.Contains("sourceSets", serviceBuild, StringComparison.Ordinal);
        Assert.Contains("generated/sources/annotations", serviceBuild, StringComparison.Ordinal);
        Assert.Contains("runtimeOnly(", serviceBuild, StringComparison.Ordinal);
        Assert.Contains("testImplementation(", serviceBuild, StringComparison.Ordinal);
        Assert.Contains("[libraries]", catalog, StringComparison.Ordinal);
        Assert.Contains("[plugins]", catalog, StringComparison.Ordinal);

        Assert.True(File.Exists(ResolveFixture(
            "GradleKotlinMultiProject/service/src/test/java/org/aegis/fixture/gradle/HandlerTest.java.fixture")));
        Assert.True(File.Exists(ResolveFixture(
            "GradleKotlinMultiProject/service/build/generated/sources/annotations/org/aegis/fixture/generated/CommandMapperImpl.java.fixture")));
        var unresolvedSource = File.ReadAllText(ResolveFixture(
            "GradleKotlinMultiProject/service/src/main/java/org/aegis/fixture/gradle/UnresolvedCall.java.fixture"));
        Assert.Contains("MissingExternalClient.connect()", unresolvedSource, StringComparison.Ordinal);
    }

    private static string ResolveFixture(string relativePath) =>
        Path.Combine(FixtureRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string? ChildValue(XElement element, string localName) =>
        element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == localName)
            ?.Value.Trim();
}
