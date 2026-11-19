using BenchmarkDotNet.Attributes;
using Aegis.Architecture.Analysis.Projection;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Benchmarks.Workloads;

[MemoryDiagnoser]
public class ArchitectureRollupBenchmarks
{
    private ArchitectureGraphProjection _projection = null!;

    [Params(100, 1_000)]
    public int ComponentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var context = ArchitectureBenchmarkContextFactory.CreateProjectionContext(ComponentCount);
        _projection = UnifiedArchitectureGraphProjector.Project(context);
        var first = ArchitectureGraphRollup.Apply(_projection, "com.aegis", 2);
        var second = ArchitectureGraphRollup.Apply(_projection, "com.aegis", 2);

        if (!first.Nodes.Select(node => node.GroupKey).SequenceEqual(second.Nodes.Select(node => node.GroupKey), StringComparer.Ordinal)
            || !first.Edges.Select(edge => edge.Id).SequenceEqual(second.Edges.Select(edge => edge.Id), StringComparer.Ordinal))
            throw new InvalidOperationException("Roll-up workload is not deterministic.");
    }

    [Benchmark]
    public ArchitectureGraphRollupResult RollUp() => ArchitectureGraphRollup.Apply(_projection, "com.aegis", 2);
}
