using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class FileDiscoveryServiceTests
{
    [Fact]
    public void GetScopedFiles_returns_only_files_with_requested_extensions()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "src"));

        var csharpFile = Path.Combine(directory.Path, "src", "Widget.cs");
        var jsonFile = Path.Combine(directory.Path, "src", "settings.json");
        var textFile = Path.Combine(directory.Path, "src", "notes.txt");

        File.WriteAllText(csharpFile, "class Widget { }");
        File.WriteAllText(jsonFile, "{}");
        File.WriteAllText(textFile, "notes");

        var files = FileDiscoveryService.GetScopedFiles(
                directory.Path,
                [".cs", ".json"])
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(2, files.Count);
        Assert.Contains(Path.GetFullPath(csharpFile), files);
        Assert.Contains(Path.GetFullPath(jsonFile), files);
        Assert.DoesNotContain(Path.GetFullPath(textFile), files);
    }

    [Fact]
    public void GetScopedFiles_excludes_generated_and_dependency_directories()
    {
        using var directory = new TemporaryDirectory();

        var included = Path.Combine(directory.Path, "src", "Widget.cs");
        var binFile = Path.Combine(directory.Path, "bin", "Debug", "Widget.cs");
        var objFile = Path.Combine(directory.Path, "obj", "Generated.cs");
        var nodeFile = Path.Combine(directory.Path, "node_modules", "pkg", "index.js");
        var gitFile = Path.Combine(directory.Path, ".git", "hooks", "sample.cs");

        foreach (var file in new[] { included, binFile, objFile, nodeFile, gitFile })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "// fixture");
        }

        var files = FileDiscoveryService.GetScopedFiles(
                directory.Path,
                [".cs", ".js"])
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(Path.GetFullPath(included), files);
        Assert.DoesNotContain(Path.GetFullPath(binFile), files);
        Assert.DoesNotContain(Path.GetFullPath(objFile), files);
        Assert.DoesNotContain(Path.GetFullPath(nodeFile), files);
        Assert.DoesNotContain(Path.GetFullPath(gitFile), files);
    }

    [Fact]
    public void GetScopedFiles_matches_file_extensions_case_insensitively()
    {
        using var directory = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "Widget.CS");
        File.WriteAllText(file, "class Widget { }");

        var files = FileDiscoveryService.GetScopedFiles(directory.Path, [".cs"]).ToArray();

        Assert.Contains(files, path => Path.GetFullPath(path) == Path.GetFullPath(file));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AegisArchitectureTests_{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
