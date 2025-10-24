using Aegis.Core.Analysis.Detectors;
using Aegis.Shared.Models;

namespace Aegis.Core.Analysis;

public static class ProjectContextDetector
{
    public static ProjectContext Detect(string rootPath)
    {
        var ctx = new ProjectContext
        {
            DetectedAt = DateTime.UtcNow,
            DetectorVersion = "1.3.0",
            DetectionStrategy = "Hybrid",
            RootPath = rootPath
        };

        var files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                             .Where(f => !FileExclusions.IsExcluded(f))
                             .ToList();

        ctx.FileCount = files.Count;
        ctx.IsMultiModule = files.Any(f =>
            f.EndsWith(".csproj") || f.EndsWith("package.json") || f.EndsWith("pom.xml"));

        // 🧩 Modularized detection sequence
        LanguageAndBuildDetector.Analyze(ctx, files);
        FrameworkDetector.Analyze(ctx, files);
        DomainAndLayerDetector.Analyze(ctx, files);
        DeployabilityDetector.Analyze(ctx, files);
        NatureDetector.Analyze(ctx, files);
        ArchitectureDetector.Analyze(ctx);
        EntryPointDetector.Analyze(ctx, files);
        ConfidenceEvaluator.Evaluate(ctx);
        MetadataBuilder.Populate(ctx, files);

        return ctx;
    }
}
