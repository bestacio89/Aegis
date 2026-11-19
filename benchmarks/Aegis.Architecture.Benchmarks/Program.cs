using BenchmarkDotNet.Running;
using Aegis.Architecture.Benchmarks.Workloads;

BenchmarkSwitcher.FromAssembly(typeof(ArchitectureProjectionBenchmarks).Assembly).Run(args);
