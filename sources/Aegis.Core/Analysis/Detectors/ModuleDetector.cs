using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class ModuleDetector
{
    public static void Analyze(
        ProjectArchitectureContext ctx,
        List<string> files)
    {
        var modulePaths = files
            .Select(Path.GetDirectoryName)
            .Where(x => x != null)
            .Select(x => x!)
            .Distinct()
            .Where(IsModuleRoot)
            .ToList();


        foreach (var path in modulePaths)
        {
            var module = new ProjectModuleContext
            {
                Name = Path.GetFileName(path),
                Path = path,
                Type = DetectModuleType(path)
            };


            module.Languages =
                files
                .Where(f => f.StartsWith(path))
                .Select(GetLanguage)
                .Distinct()
                .ToList();


            ctx.Modules.Add(module);
        }
    }


    private static bool IsModuleRoot(string path)
    {
        return
            File.Exists(Path.Combine(path, "*.csproj")) ||
            File.Exists(Path.Combine(path, "package.json")) ||
            File.Exists(Path.Combine(path, "pom.xml"));
    }


    private static string GetLanguage(string file)
    {
        if (file.EndsWith(".cs"))
            return "C#";

        if (file.EndsWith(".java"))
            return "Java";

        if (file.EndsWith(".py"))
            return "Python";

        if (file.EndsWith(".ts"))
            return "TypeScript";

        return "Unknown";
    }


    private static string DetectModuleType(string path)
    {
        var name = path.ToLowerInvariant();


        if (name.Contains("api"))
            return "Api";

        if (name.Contains("application"))
            return "Application";

        if (name.Contains("domain"))
            return "Domain";

        if (name.Contains("infra"))
            return "Infrastructure";

        if (name.Contains("test"))
            return "Test";

        return "Unknown";
    }
}