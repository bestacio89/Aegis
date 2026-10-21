using Aegis.Shared.Architecture.Models;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Aegis.Architecture.Analysis.Detectors;

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
                DetectDotNetFramework(ctx, deps, files);
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

        if (!string.IsNullOrWhiteSpace(ctx.Framework)
            && !ctx.DetectedFrameworks.Contains(
                ctx.Framework,
                StringComparer.OrdinalIgnoreCase))
        {
            ctx.DetectedFrameworks.Add(ctx.Framework);
        }
    }

    private static void DetectDotNetFramework(
        ProjectArchitectureContext ctx,
        HashSet<string> deps,
        IReadOnlyCollection<string> files)
    {
        // A .razor file is positive evidence of Blazor UI. Keep that signal
        // alongside backend framework evidence instead of collapsing a mixed
        // repository into one language-derived domain.
        var hasBlazorComponents = files.Any(file =>
            Path.GetExtension(file).Equals(
                ".razor",
                StringComparison.OrdinalIgnoreCase));

        if (hasBlazorComponents)
        {
            ctx.DetectedFrameworks.Add("Blazor");
        }

        var hasWebSdk = files
            .Where(file => file.EndsWith(
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
            .Any(projectFile =>
            {
                try
                {
                    var project = XDocument.Load(projectFile);
                    var sdk = project.Root?.Attribute("Sdk")?.Value;
                    return sdk?.Contains(
                        "Microsoft.NET.Sdk.Web",
                        StringComparison.OrdinalIgnoreCase) == true;
                }
                catch (System.Xml.XmlException)
                {
                    // Malformed project files are handled by configuration
                    // diagnostics; framework detection should not crash scanning.
                    return false;
                }
                catch (IOException)
                {
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            });

        var hasAspNetCore = hasWebSdk || deps.Any(d =>
            d.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase));

        if (hasAspNetCore
            && !ctx.DetectedFrameworks.Contains(
                "ASP.NET Core",
                StringComparer.OrdinalIgnoreCase))
        {
            ctx.DetectedFrameworks.Add("ASP.NET Core");
        }

        if (hasBlazorComponents)
        {
            // Prefer Blazor as the primary UI framework while retaining ASP.NET
            // Core as additional evidence when this is a server-hosted app.
            ctx.Framework = "Blazor";
        }
        else if (hasAspNetCore)
        {
            ctx.Framework = "ASP.NET Core";
        }
        else if (deps.Any(d =>
                     d.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Framework = ".NET (EF Core)";
        }
        else
        {
            ctx.Framework = ".NET";
        }
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
