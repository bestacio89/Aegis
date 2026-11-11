using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class NatureDetector
{
    public static void Analyze(ProjectArchitectureContext ctx, List<string> files)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(files);

        if (ctx.ProductionModules.Count > 1)
        {
            ctx.Nature = "MultiProject";
            return;
        }

        var project = ctx.ProductionModules.SingleOrDefault();

        if (project is null && ctx.TestModules.Count > 0)
        {
            ctx.Nature = "TestSuite";
            return;
        }
        if (project is not null)
        {
            ctx.Nature = project.ProjectKind switch
            {
                "Test" => "TestSuite",
                "ClassLibrary" => "Library",
                "WebApplication" => "Service",
                "FrontendApplication" => "FrontendApp",
                "DesktopApplication" => "DesktopApp",
                "Executable" => "Application",
                _ => "Generic"
            };

            if (ctx.Nature != "Generic")
                return;
        }

        bool sdk = files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}sdk{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        bool tests = files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        bool lib = files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        if (tests) ctx.Nature = "TestSuite";
        else if (sdk) ctx.Nature = "SDK";
        else if (lib) ctx.Nature = "Library";
        else if (ctx.DomainType == "Backend") ctx.Nature = "Service";
        else if (ctx.DomainType == "Frontend") ctx.Nature = "FrontendApp";
        else if (ctx.DomainType == "FullStack") ctx.Nature = "FullStackApp";
        else ctx.Nature = "Generic";
    }
}
