using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class LocalJvmClasspathResolverTests
{
    [Fact]
    public void Resolves_direct_maven_artifacts_without_claiming_transitive_completeness()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "m2");
        var artifact = Path.Combine(repository, "org", "example", "api", "1.2.3", "api-1.2.3.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(artifact)!);
        File.WriteAllBytes(artifact, Array.Empty<byte>());

        try
        {
            var resolver = new LocalJvmClasspathResolver(repository, Path.Combine(root, "gradle"));
            var module = new ProjectModuleContext
            {
                MavenModel = new MavenProjectModel
                {
                    Dependencies =
                    [
                        new MavenProjectModel.Dependency(
                            "org.example", "api", "1.2.3", "compile", "jar", null,
                            false, false, false, [], "pom.xml", false)
                    ]
                }
            };

            var result = resolver.Resolve(module, [module], includeTestCode: true);

            Assert.Equal(artifact, Assert.Single(result.Entries));
            Assert.True(result.HasDeclaredDependencies);
            Assert.False(result.IsComplete);
            Assert.Empty(result.UnresolvedDependencies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reports_missing_direct_gradle_artifacts_without_guessing_cache_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var resolver = new LocalJvmClasspathResolver(
                Path.Combine(root, "m2"),
                Path.Combine(root, "gradle"));
            var module = new ProjectModuleContext
            {
                GradleBuildModel = new GradleBuildModel
                {
                    Dependencies =
                    [
                        new GradleBuildModel.Dependency(
                            "implementation", "org.example", "missing-api", "4.5.6",
                            null, null, false, false, false, "implementation(...)")
                    ]
                }
            };

            var result = resolver.Resolve(module, [module], includeTestCode: true);

            Assert.Empty(result.Entries);
            Assert.Equal("org.example:missing-api:4.5.6", Assert.Single(result.UnresolvedDependencies));
            Assert.False(result.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Treats_modules_without_external_dependencies_as_complete()
    {
        var resolver = new LocalJvmClasspathResolver(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "m2"),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "gradle"));

        var result = resolver.Resolve(new ProjectModuleContext(), Array.Empty<ProjectModuleContext>(), includeTestCode: true);

        Assert.True(result.IsComplete);
        Assert.False(result.HasDeclaredDependencies);
        Assert.Empty(result.Entries);
    }
}
