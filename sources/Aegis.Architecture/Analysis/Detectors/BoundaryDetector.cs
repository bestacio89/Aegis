using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class BoundaryDetector
{
    public static void Analyze(ProjectArchitectureContext context)
    {
        context.Boundaries ??= new List<ArchitectureBoundaryContext>();
        context.Boundaries.Clear();

        if (context.PatternsDetected.Contains("Domain Driven Design")
            || string.Equals(context.ArchitectureStyle, "Clean", StringComparison.OrdinalIgnoreCase))
        {
            ConfigureCleanArchitecture(context);
            return;
        }

        ConfigureDefaultLayering(context);
    }

    private static void ConfigureCleanArchitecture(ProjectArchitectureContext context)
    {
        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Domain",
            AllowedDependencies = [],
            ForbiddenDependencies = ["Application", "Infrastructure", "Api", "Presentation", "Database"]
        });

        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Application",
            AllowedDependencies = ["Domain", "Contracts"],
            ForbiddenDependencies = ["Api", "Presentation", "Infrastructure"]
        });

        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Infrastructure",
            AllowedDependencies = ["Application", "Domain", "Contracts"]
        });

        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Api",
            AllowedDependencies = ["Application", "Domain", "Infrastructure", "Contracts"]
        });

        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Presentation",
            AllowedDependencies = ["Application", "Contracts", "Client"],
            ForbiddenDependencies = ["Infrastructure", "Database"]
        });

        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Contracts",
            AllowedDependencies = []
        });
    }

    private static void ConfigureDefaultLayering(ProjectArchitectureContext context)
    {
        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Domain",
            AllowedDependencies = []
        });

        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Application",
            AllowedDependencies = ["Domain", "Contracts"],
            ForbiddenDependencies = ["Api", "Presentation"]
        });

        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Infrastructure",
            AllowedDependencies = ["Domain", "Application", "Contracts"]
        });

        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Api",
            AllowedDependencies = ["Application", "Domain", "Infrastructure", "Contracts"]
        });

        context.Boundaries.Add(new ArchitectureBoundaryContext
        {
            SourceLayer = "Contracts",
            AllowedDependencies = []
        });
    }
}
