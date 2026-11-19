using BenchmarkDotNet.Attributes;
using Aegis.Architecture.Analysis.Projection;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Benchmarks.Workloads;

[MemoryDiagnoser]
public class ArchitecturePipelineBenchmarks
{
    private ProjectArchitectureContext _context = null!;

    [Params(100, 1_000)]
    public int ComponentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _context = ArchitectureBenchmarkContextFactory.CreateProjectionContext(ComponentCount);
        var first = ExecutePipeline();
        var second = ExecutePipeline();

        if (!first.Nodes.Select(node => node.GroupKey).SequenceEqual(
                second.Nodes.Select(node => node.GroupKey),
                StringComparer.Ordinal)
            || !first.Edges.Select(GetStableEdgeIdentity).SequenceEqual(
                second.Edges.Select(GetStableEdgeIdentity)))
            throw new InvalidOperationException("Combined projection and roll-up workload is not deterministic.");
    }

    [Benchmark]
    public ArchitectureGraphRollupResult ProjectAndRollUp() => ExecutePipeline();

    private ArchitectureGraphRollupResult ExecutePipeline()
    {
        var projection = UnifiedArchitectureGraphProjector.Project(_context);
        return ArchitectureGraphRollup.Apply(projection, "com.aegis", 2);
    }

    private static (string Source, string Target, ArchitectureGraphEdgeDomain Domain, string Scope, string LeafEdgeIds) GetStableEdgeIdentity(
        ArchitectureGraphRollupEdge edge) =>
        (
            edge.SourceGroupKey,
            edge.TargetGroupKey,
            edge.Domain,
            edge.Scope,
            string.Join("|", edge.LeafEdgeIds)
        );
}
