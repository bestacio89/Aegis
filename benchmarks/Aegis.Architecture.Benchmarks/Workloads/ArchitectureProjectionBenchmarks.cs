using BenchmarkDotNet.Attributes;
using Aegis.Architecture.Analysis.Projection;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Benchmarks.Workloads;

[MemoryDiagnoser]
public class ArchitectureProjectionBenchmarks
{
    private ProjectArchitectureContext _context = null!;

    [Params(100, 1_000)]
    public int ComponentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _context = ArchitectureBenchmarkContextFactory.CreateProjectionContext(ComponentCount);
        var first = UnifiedArchitectureGraphProjector.Project(_context);
        var second = UnifiedArchitectureGraphProjector.Project(_context);

        if (first.Nodes.Count != second.Nodes.Count || first.Edges.Count != second.Edges.Count
            || !first.Nodes.Select(node => node.Id).SequenceEqual(second.Nodes.Select(node => node.Id), StringComparer.Ordinal)
            || !first.Edges.Select(edge => edge.Id).SequenceEqual(second.Edges.Select(edge => edge.Id), StringComparer.Ordinal))
            throw new InvalidOperationException("Projection workload is not deterministic.");

        if (first.Edges.Count != ComponentCount - 1)
            throw new InvalidOperationException("Projection workload produced an unexpected edge count.");
    }

    [Benchmark(Baseline = true)]
    public ArchitectureGraphProjection Project() => UnifiedArchitectureGraphProjector.Project(_context);
}
