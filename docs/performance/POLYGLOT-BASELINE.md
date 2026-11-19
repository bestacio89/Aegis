# Polyglot Architecture Performance Baseline

## Purpose

This suite establishes reproducible measurements for the existing architecture discovery and graph pipeline. It is a measurement instrument, not a performance guarantee or regression gate. No optimization or performance budget is implied by the initial results.

## Workloads

| Benchmark | Input | Boundary measured |
|---|---|---|
| Repository discovery | Generated C# repository with 100 or 500 source files | Filesystem enumeration and in-process context discovery |
| Graph projection | Preconstructed context with 100 or 1,000 modules/components and a dependency chain | Unified graph projection only |
| Graph roll-up | Preconstructed projection with 100 or 1,000 package nodes and a dependency chain | Graph aggregation only |

Projection and roll-up fixtures are generated deterministically. Benchmark setup executes each operation twice and verifies stable node/edge identity before measurement. Discovery fixtures contain generated project and source files only. No project build, package restore, or repository build script is executed.

Java and Kotlin worker process startup and semantic analysis are excluded from these in-process measurements. They require a separate process-level workload with explicit worker packaging, startup, shutdown, and environment measurements; their cost must not be inferred from these results.

## Execution

Run from the repository root:

~~~powershell
dotnet run --project benchmarks/Aegis.Architecture.Benchmarks/Aegis.Architecture.Benchmarks.csproj --configuration Release
~~~

The manual GitHub Actions workflow runs the same suite and uploads the complete BenchmarkDotNet artifact directory.

## Interpretation

BenchmarkDotNet reports runtime environment, distribution statistics, and memory diagnostics. Compare results only when workload parameters, runtime, operating system, architecture, and runner characteristics are materially equivalent.

Hosted-runner results are informational. They must not be used as hard PR thresholds because runner contention and environment variation are uncontrolled. Keep raw reports and environment metadata together. Do not compare aggregate discovery measurements with projection-only or roll-up-only measurements.

## Initial baseline status

No baseline numbers or regression thresholds are published by this change. The first successful benchmark run establishes the initial measurement artifact. Any future budget requires repeated measurements on a controlled environment and an explicitly approved statistical policy.
