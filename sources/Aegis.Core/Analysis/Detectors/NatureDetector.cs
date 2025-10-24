using Aegis.Shared.Models;

namespace Aegis.Core.Analysis.Detectors;

internal static class NatureDetector
{
    public static void Analyze(ProjectContext ctx, List<string> files)
    {
        bool sdk = files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}sdk{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        bool tests = files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        bool lib = files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        if (tests) ctx.Nature = "TestSuite";
        else if (sdk) ctx.Nature = "SDK";
        else if (lib) ctx.Nature = "Library";
        else if (ctx.DomainType == "Backend") ctx.Nature = "Service";
        else if (ctx.DomainType == "Frontend") ctx.Nature = "FrontendApp";
        else ctx.Nature = "Generic";
    }
}
