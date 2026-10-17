using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class BoundaryDetector
{
    public static void Analyze(ProjectArchitectureContext ctx)
    {
        ctx.Boundaries ??= new List<ArchitectureBoundaryContext>();

        if (ctx.PatternsDetected.Contains("Domain Driven Design")
            ||
            ctx.ArchitectureStyle == "Clean")
        {
            ConfigureCleanArchitecture(ctx);
            return;
        }


        ConfigureDefaultLayering(ctx);
    }


    private static void ConfigureCleanArchitecture(
        ProjectArchitectureContext ctx)
    {
        ctx.Boundaries.Add(
            new ArchitectureBoundaryContext
            {
                SourceLayer = "Domain",

                AllowedDependencies =
                [
                    "Application"
                ],

                ForbiddenDependencies =
                [
                    "Infrastructure",
                    "Api",
                    "Database"
                ]
            });


        ctx.Boundaries.Add(
            new ArchitectureBoundaryContext
            {
                SourceLayer = "Application",

                AllowedDependencies =
                [
                    "Domain",
                    "Infrastructure"
                ],

                ForbiddenDependencies =
                [
                    "Api"
                ]
            });


        ctx.Boundaries.Add(
            new ArchitectureBoundaryContext
            {
                SourceLayer = "Infrastructure",

                AllowedDependencies =
                [
                    "Application",
                    "Domain"
                ]
            });
    }


    private static void ConfigureDefaultLayering(
        ProjectArchitectureContext ctx)
    {
        ctx.Boundaries.Add(
            new ArchitectureBoundaryContext
            {
                SourceLayer = "Domain",

                AllowedDependencies =
                [
                    "Domain"
                ]
            });


        ctx.Boundaries.Add(
            new ArchitectureBoundaryContext
            {
                SourceLayer = "Application",

                AllowedDependencies =
                [
                    "Domain",
                    "Application"
                ]
            });


        ctx.Boundaries.Add(
            new ArchitectureBoundaryContext
            {
                SourceLayer = "Infrastructure",

                AllowedDependencies =
                [
                    "Domain",
                    "Application",
                    "Infrastructure"
                ]
            });
    }
}