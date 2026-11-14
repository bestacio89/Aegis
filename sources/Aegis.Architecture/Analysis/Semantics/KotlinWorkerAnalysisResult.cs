using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Semantic evidence plus transient compiler output metadata returned by the Kotlin worker.
/// The output path is execution-local and must never be serialized into reports.
/// </summary>
public sealed record KotlinWorkerAnalysisResult(
    SemanticAnalysisRun Run,
    string? CompilationOutputDirectory,
    bool CompilationSucceeded);
