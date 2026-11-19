using BenchmarkDotNet.Attributes;
using Aegis.Architecture.Analysis;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Benchmarks.Workloads;

[MemoryDiagnoser]
public class ArchitectureDiscoveryBenchmarks
{
    private string _rootPath = null!;

    [Params(100, 500)]
    public int SourceFileCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "AegisArchitectureBenchmarks", Guid.NewGuid().ToString("N"));
        ArchitectureBenchmarkContextFactory.CreateDiscoveryFixture(_rootPath, SourceFileCount);

        var first = ProjectArchitectureContextDetector.Detect(_rootPath);
        var second = ProjectArchitectureContextDetector.Detect(_rootPath);
        if (first.FileCount != second.FileCount || first.FileCount < SourceFileCount)
            throw new InvalidOperationException("Discovery fixture did not produce stable file counts.");
    }

    [Benchmark]
    public ProjectArchitectureContext DetectRepository() => ProjectArchitectureContextDetector.Detect(_rootPath);

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }
}
