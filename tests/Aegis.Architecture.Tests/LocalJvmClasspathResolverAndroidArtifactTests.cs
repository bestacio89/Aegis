using System.IO.Compression;
using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class LocalJvmClasspathResolverAndroidArtifactTests
{
    [Fact]
    public void Resolves_gradle_aar_classes_and_nested_jars_from_the_local_cache()
    {
        var root = CreateRoot();
        try
        {
            var gradleCache = Path.Combine(root, "gradle");
            var aarPath = Path.Combine(
                gradleCache, "org.example", "android-api", "1.2.3", "cache-hash",
                "android-api-1.2.3.aar");
            CreateAar(aarPath, true, "libs/annotations.jar", "libs/runtime.jar");

            var resolver = new LocalJvmClasspathResolver(
                Path.Combine(root, "m2"),
                gradleCache,
                Path.Combine(root, "extracted"));
            var module = CreateModule(new GradleBuildModel.Dependency(
                "implementation", "org.example", "android-api", "1.2.3",
                null, null, false, false, false, "implementation(...)"));

            var result = resolver.Resolve(module, [module], includeTestCode: false);

            Assert.Equal(3, result.Entries.Count);
            Assert.Contains(result.Entries, path => Path.GetFileName(path) == "classes.jar");
            Assert.Contains(result.Entries, path => Path.GetFileName(path).EndsWith("-annotations.jar", StringComparison.Ordinal));
            Assert.Contains(result.Entries, path => Path.GetFileName(path).EndsWith("-runtime.jar", StringComparison.Ordinal));
            Assert.All(result.Entries, path => Assert.True(File.Exists(path)));
            Assert.False(result.IsComplete);
            Assert.Empty(result.UnresolvedDependencies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolves_maven_aar_using_the_same_generic_classpath_contract()
    {
        var root = CreateRoot();
        try
        {
            var repository = Path.Combine(root, "m2");
            var aarPath = Path.Combine(
                repository, "org", "example", "android-api", "1.2.3",
                "android-api-1.2.3.aar");
            CreateAar(aarPath, true);

            var resolver = new LocalJvmClasspathResolver(
                repository,
                Path.Combine(root, "gradle"),
                Path.Combine(root, "extracted"));
            var module = new ProjectModuleContext
            {
                MavenModel = new MavenProjectModel
                {
                    Dependencies =
                    [
                        new MavenProjectModel.Dependency(
                            "org.example", "android-api", "1.2.3", "compile", "aar", null,
                            false, false, false, [], "pom.xml", false)
                    ]
                }
            };

            var result = resolver.Resolve(module, [module], includeTestCode: false);

            Assert.Equal("classes.jar", Path.GetFileName(Assert.Single(result.Entries)));
            Assert.False(result.IsComplete);
            Assert.Empty(result.UnresolvedDependencies);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Reports_malformed_local_aar_as_unresolved_instead_of_throwing()
    {
        var root = CreateRoot();
        try
        {
            var gradleCache = Path.Combine(root, "gradle");
            var aarPath = Path.Combine(
                gradleCache, "org.example", "broken", "1.0.0", "cache-hash",
                "broken-1.0.0.aar");
            Directory.CreateDirectory(Path.GetDirectoryName(aarPath)!);
            File.WriteAllText(aarPath, "not an archive");

            var resolver = new LocalJvmClasspathResolver(
                Path.Combine(root, "m2"),
                gradleCache,
                Path.Combine(root, "extracted"));
            var module = CreateModule(new GradleBuildModel.Dependency(
                "implementation", "org.example", "broken", "1.0.0",
                null, null, false, false, false, "implementation(...)"));

            var result = resolver.Resolve(module, [module], includeTestCode: false);

            Assert.Empty(result.Entries);
            Assert.Equal("org.example:broken:1.0.0", Assert.Single(result.UnresolvedDependencies));
            Assert.False(result.IsComplete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Extractor_ignores_non_compiler_entries_and_never_uses_archive_paths()
    {
        var root = CreateRoot();
        try
        {
            var aarPath = Path.Combine(root, "library.aar");
            CreateAar(aarPath, true, "../escape.jar", "libs/nested/ignored.jar");

            var entries = new AarArtifactExtractor(Path.Combine(root, "extracted")).Extract(aarPath);

            Assert.Equal("classes.jar", Path.GetFileName(Assert.Single(entries)));
            Assert.DoesNotContain("escape.jar", Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ProjectModuleContext CreateModule(GradleBuildModel.Dependency dependency) => new()
    {
        GradleBuildModel = new GradleBuildModel { Dependencies = [dependency] }
    };

    private static void CreateAar(string path, bool includeClasses, params string[] additionalEntries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        if (includeClasses)
            AddEntry(archive, "classes.jar", [0x50, 0x4b, 0x03, 0x04]);

        foreach (var entry in additionalEntries)
            AddEntry(archive, entry, [0x50, 0x4b, 0x03, 0x04]);

        AddEntry(archive, "AndroidManifest.xml", [0x01, 0x02]);
        AddEntry(archive, "res/layout/main.xml", [0x03, 0x04]);
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
