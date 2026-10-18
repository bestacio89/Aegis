using Aegis.Architecture.Analysis;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ProjectArchitectureContextDetectorTests
{
    [Fact]
    public void Detect_preserves_project_identity_and_initializes_detection_metadata()
    {
        using var directory = new TemporaryDirectory();

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal(Path.GetFileName(directory.Path), context.ProjectName);
        Assert.Equal(directory.Path, context.RootPath);
        Assert.Equal(0, context.FileCount);
        Assert.Equal("1.5.0", context.DetectorVersion);
        Assert.Equal("Hybrid", context.DetectionStrategy);
        Assert.Equal(context.ProjectName, context.MetadataMap["ProjectName"]);
        Assert.Equal("0", context.MetadataMap["FileCount"]);
        Assert.True(context.Confidence >= 0);
        Assert.True(context.Confidence <= 1);
    }

    [Fact]
    public void Detect_counts_project_files_but_excludes_generated_output()
    {
        using var directory = new TemporaryDirectory();

        var source = Path.Combine(directory.Path, "src", "Program.cs");
        var project = Path.Combine(directory.Path, "Sample.csproj");
        var generated = Path.Combine(directory.Path, "obj", "Generated.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(generated)!);
        File.WriteAllText(source, "public static class Program { }");
        File.WriteAllText(project, "<Project />");
        File.WriteAllText(generated, "generated");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal(2, context.FileCount);
        Assert.Equal("2", context.MetadataMap["FileCount"]);
        Assert.Equal(directory.Path, context.RootPath);
    }

    [Fact]
    public void Detect_handles_a_project_with_multiple_manifest_files()
    {
        using var directory = new TemporaryDirectory();

        File.WriteAllText(Path.Combine(directory.Path, "package.json"), """{"name":"fixture","dependencies":{}}""");
        File.WriteAllText(Path.Combine(directory.Path, "requirements.txt"), "pytest==8.0.0");
        File.WriteAllText(Path.Combine(directory.Path, "Dockerfile"), "FROM scratch");

        var context = ProjectArchitectureContextDetector.Detect(directory.Path);

        Assert.Equal(3, context.FileCount);
        Assert.NotNull(context.MetadataMap);
        Assert.Equal("3", context.MetadataMap["FileCount"]);
        Assert.NotNull(context.DetectionStrategy);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"AegisContextTests_{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
