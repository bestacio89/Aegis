using Aegis.Shared.Models;

namespace Aegis.Shared.Models;
public record MultiProjectContext(
    List<ProjectContext> DetectedContexts,
    double OverallConfidence = 1.0
);
