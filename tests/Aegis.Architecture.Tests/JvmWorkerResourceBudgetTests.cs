using Aegis.Architecture.Analysis.Semantics;
using Aegis.Shared.Architecture.Models;
using FluentAssertions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class JvmWorkerResourceBudgetTests
{
    [Fact]
    public void ValidateRequest_accepts_a_source_file_at_the_per_file_boundary()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile("Boundary.kt", JvmWorkerResourceBudget.MaximumSourceFileBytes);
        var action = () => JvmWorkerResourceBudget.ValidateRequest(CreateRequest(source), "Kotlin");
        action.Should().NotThrow();
    }

    [Fact]
    public void ValidateRequest_rejects_a_source_file_over_the_per_file_boundary()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile("Oversized.kt", JvmWorkerResourceBudget.MaximumSourceFileBytes + 1);
        var action = () => JvmWorkerResourceBudget.ValidateRequest(CreateRequest(source), "Java");
        action.Should().Throw<InvalidOperationException>().WithMessage("*per-file budget*");
    }

    [Fact]
    public void ValidateRequest_rejects_aggregate_source_bytes_over_the_budget()
    {
        using var directory = new TemporaryDirectory();
        var sources = Enumerable.Range(0, 33)
            .Select(index => directory.CreateFile(
                $"Source{index}.kt",
                index < 32 ? JvmWorkerResourceBudget.MaximumSourceFileBytes : 1))
            .ToArray();
        var action = () => JvmWorkerResourceBudget.ValidateRequest(CreateRequest(sources), "Kotlin");
        action.Should().Throw<InvalidOperationException>().WithMessage("*aggregate budget*");
    }

    [Fact]
    public void ValidateRequest_rejects_more_than_the_allowed_source_file_count()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile("Service.kt", 1);
        var sources = Enumerable.Repeat(source, JvmWorkerResourceBudget.MaximumSourceFiles + 1);
        var action = () => JvmWorkerResourceBudget.ValidateRequest(CreateRequest(sources), "Java");
        action.Should().Throw<InvalidOperationException>().WithMessage("*source files*");
    }

    [Fact]
    public void ValidateManifestBytes_accepts_the_exact_boundary_and_rejects_one_byte_over()
    {
        var exact = () => JvmWorkerResourceBudget.ValidateManifestBytes(
            JvmWorkerResourceBudget.MaximumManifestBytes, "Java");
        var over = () => JvmWorkerResourceBudget.ValidateManifestBytes(
            JvmWorkerResourceBudget.MaximumManifestBytes + 1, "Kotlin");
        exact.Should().NotThrow();
        over.Should().Throw<InvalidOperationException>().WithMessage("*UTF-8 bytes*");
    }

    [Fact]
    public void JvmArguments_define_the_fixed_process_budget()
    {
        JvmWorkerResourceBudget.JvmArguments.Should().Equal(
            "-Xms64m", "-Xmx1024m", "-XX:MaxMetaspaceSize=384m",
            "-XX:MaxDirectMemorySize=128m", "-XX:ActiveProcessorCount=2",
            "-XX:+ExitOnOutOfMemoryError");
    }

    private static SemanticAnalysisRequest CreateRequest(string sourceFile) => CreateRequest([sourceFile]);

    private static SemanticAnalysisRequest CreateRequest(IEnumerable<string> sourceFiles) =>
        new() { SourceFiles = sourceFiles.ToArray() };

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("aegis-jvm-budget-");

        public string CreateFile(string name, long length)
        {
            var path = Path.Combine(_directory.FullName, name);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.SetLength(length);
            return path;
        }

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
