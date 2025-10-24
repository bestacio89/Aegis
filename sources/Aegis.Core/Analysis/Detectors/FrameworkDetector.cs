using Aegis.Shared.Architecture.Models;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Aegis.Core.Analysis.Detectors;

internal static class FrameworkDetector
{
    public static void Analyze(ProjectArchitectureContext ctx, List<string> files)
    {
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (ctx.Language)
        {
            case "C#":
                foreach (var proj in files.Where(f => f.EndsWith(".csproj")))
                {
                    var xml = XDocument.Load(proj);
                    foreach (var pkg in xml.Descendants("PackageReference"))
                    {
                        var name = pkg.Attribute("Include")?.Value ?? pkg.Attribute("Update")?.Value;
                        if (!string.IsNullOrWhiteSpace(name))
                            deps.Add(name);
                    }
                }
                ctx.DetectedDependencies = deps.ToList();
                DetectDotNetFramework(ctx, deps);
                break;

            case "Java":
                DetectJavaFramework(ctx, files, deps);
                break;

            case "Python":
                DetectPythonFramework(ctx, files, deps);
                break;

            case "TypeScript/JavaScript":
                DetectNodeFramework(ctx, files, deps);
                break;
        }
    }

    private static void DetectDotNetFramework(ProjectArchitectureContext ctx, HashSet<string> deps)
    {
        if (deps.Any(d => d.Contains("EntityFrameworkCore"))) ctx.Framework = ".NET (EF Core)";
        else if (deps.Any(d => d.Contains("AspNetCore"))) ctx.Framework = "ASP.NET Core";
        else ctx.Framework = ".NET";
    }

    private static void DetectJavaFramework(ProjectArchitectureContext ctx, List<string> files, HashSet<string> deps)
    {
        if (files.Any(f => f.Contains("spring", StringComparison.OrdinalIgnoreCase)))
            ctx.Framework = "Spring Boot";
        else if (deps.Any(d => d.Contains("hibernate"))) ctx.Framework = "Hibernate";
        else ctx.Framework = "Java SE";
    }

    private static void DetectPythonFramework(ProjectArchitectureContext ctx, List<string> files, HashSet<string> deps)
    {
        var text = string.Join("\n", files.Select(File.ReadAllText));
        if (text.Contains("flask")) ctx.Framework = "Flask";
        else if (text.Contains("django")) ctx.Framework = "Django";
        else if (text.Contains("fastapi")) ctx.Framework = "FastAPI";
        else ctx.Framework = "Python Standard";
    }

    private static void DetectNodeFramework(ProjectArchitectureContext ctx, List<string> files, HashSet<string> deps)
    {
        foreach (var pkg in files.Where(f => f.EndsWith("package.json")))
        {
            var json = JsonNode.Parse(File.ReadAllText(pkg))?.AsObject();
            foreach (var key in new[] { "dependencies", "devDependencies" })
                if (json?[key] is JsonObject obj)
                    foreach (var kv in obj)
                        deps.Add(kv.Key);
        }

        if (deps.Contains("@angular/core")) ctx.Framework = "Angular";
        else if (deps.Contains("react")) ctx.Framework = "React";
        else if (deps.Contains("vue")) ctx.Framework = "Vue";
        else if (deps.Contains("express")) ctx.Framework = "Express";
        else ctx.Framework = "Node.js";
    }
}
