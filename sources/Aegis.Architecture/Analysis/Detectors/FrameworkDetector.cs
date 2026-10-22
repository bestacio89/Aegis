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
                foreach (var proj in files.Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var xml = XDocument.Load(proj);
                        foreach (var pkg in xml.Descendants("PackageReference"))
                        {
                            var name = pkg.Attribute("Include")?.Value ?? pkg.Attribute("Update")?.Value;
                            if (!string.IsNullOrWhiteSpace(name))
                                deps.Add(name);
                        }
                    }
                    catch (Exception exception) when (exception is System.Xml.XmlException
                        or IOException or UnauthorizedAccessException)
                    {
                        // Configuration diagnostics own malformed/unreadable
                        // project files. One bad manifest must not abort detection
                        // of other projects and frameworks in the repository.
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

        // Discover frontend frameworks independently of the repository's primary
        // language. A C# web API and an Angular/React client are separate technology
        // signals in one repository; the primary backend identity must not suppress
        // the client framework or its package inventory.
        if (!string.Equals(
                ctx.Language,
                "TypeScript/JavaScript",
                StringComparison.OrdinalIgnoreCase)
            && files.Any(file => Path.GetFileName(file).Equals(
                "package.json",
                StringComparison.OrdinalIgnoreCase)))
        {
            DetectNodeFramework(ctx, files, deps, preservePrimaryFramework: true);
            ctx.DetectedDependencies = deps.ToList();
        }

        if (!string.IsNullOrWhiteSpace(ctx.Framework))
            AddFramework(ctx, ctx.Framework);
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

        var hasUnityProjectEvidence = files.Any(file =>
            file.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith("ProjectVersion.txt", StringComparison.OrdinalIgnoreCase)
                && file.Contains($"{Path.DirectorySeparatorChar}ProjectSettings{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)
                && file.Contains($"{Path.DirectorySeparatorChar}Packages{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && SafeReadText(file).Contains("com.unity", StringComparison.OrdinalIgnoreCase));

        AddFramework(ctx, ".NET");
        if (hasBlazorComponents)
            AddFramework(ctx, "Blazor");
        if (hasUnityProjectEvidence)
            AddFramework(ctx, "Unity");

        // Read project SDK metadata independently from the repository's primary
        // framework. A mixed repository may contain Web, class-library and Unity
        // projects simultaneously.
        var hasWebSdk = files
            .Where(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Any(projectFile =>
            {
                try
                {
                    var project = XDocument.Load(projectFile);
                    var sdk = project.Root?.Attribute("Sdk")?.Value;
                    return sdk?.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) == true;
                }
                catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
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

        if (hasAspNetCore)
            AddFramework(ctx, "ASP.NET Core");

        // Keep the primary framework aligned with the project's server/runtime
        // identity when backend evidence exists. Blazor remains a first-class
        // additional framework signal and is applied to Razor facts individually.
        if (hasAspNetCore)
            ctx.Framework = "ASP.NET Core";
        else if (hasBlazorComponents)
            ctx.Framework = "Blazor";
        else if (hasUnityProjectEvidence)
            ctx.Framework = "Unity";
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

    private static void AddFramework(ProjectArchitectureContext context, string framework)
    {
        if (!context.DetectedFrameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
            context.DetectedFrameworks.Add(framework);
    }

    private static string SafeReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
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

    private static void DetectNodeFramework(
        ProjectArchitectureContext ctx,
        List<string> files,
        HashSet<string> deps,
        bool preservePrimaryFramework = false)
    {
        foreach (var packageFile in files.Where(file =>
                     Path.GetFileName(file).Equals(
                         "package.json",
                         StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var json = JsonNode.Parse(File.ReadAllText(packageFile))?.AsObject();
                foreach (var key in new[] { "dependencies", "devDependencies", "peerDependencies" })
                {
                    if (json?[key] is not JsonObject dependencies)
                        continue;

                    foreach (var dependency in dependencies)
                        deps.Add(dependency.Key);
                }
            }
            catch (Exception exception) when (
                exception is System.Text.Json.JsonException
                    or IOException
                    or UnauthorizedAccessException)
            {
                // A malformed package manifest is not evidence for a framework.
                // Continue discovering other manifests in a mixed repository.
            }
        }

        var detectedFrameworks = new List<string>();
        if (deps.Contains("@angular/core"))
            detectedFrameworks.Add("Angular");
        if (deps.Contains("react"))
            detectedFrameworks.Add("React");
        if (deps.Contains("vue"))
            detectedFrameworks.Add("Vue");
        if (deps.Contains("express"))
            detectedFrameworks.Add("Express");

        if (detectedFrameworks.Count == 0)
            detectedFrameworks.Add("Node.js");

        foreach (var framework in detectedFrameworks)
            AddFramework(ctx, framework);

        // The primary framework is a summary, not a replacement for the full
        // framework inventory. Preserve an already detected backend in mixed
        // repositories while still exposing all client framework signals.
        if (!preservePrimaryFramework)
            ctx.Framework = detectedFrameworks[0];
    }
}
