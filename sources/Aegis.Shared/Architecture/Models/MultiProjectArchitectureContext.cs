namespace Aegis.Shared.Architecture.Models;
public record MultiProjectArchitectureContext(
    List<ProjectArchitectureContext> DetectedContexts,
    double OverallConfidence = 1.0
);
