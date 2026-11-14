using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Statically selected Gradle dependency declarations for one Android variant and source scope.
/// This is declaration evidence, not a resolved compiler classpath.
/// </summary>
public sealed record AndroidVariantDependencySelection(
    string VariantName,
    AndroidVariantDependencyScope Scope,
    IReadOnlyList<GradleBuildModel.Dependency> Dependencies,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics,
    bool IsComplete);
