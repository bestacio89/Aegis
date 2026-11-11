using Aegis.Shared.Architecture.Models;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class FrameworkDetector
{
    public static void Analyze(ProjectArchitectureContext ctx, List<string> files)
    {
        ctx.FrontendFrameworkDiscoveryComplete =
            !ctx.DiscoveryDiagnostics.Any(diagnostic => diagnostic.AffectsCoverage);

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
                deps.UnionWith(ctx.DetectedDependencies);
                break;

            case "Python":
                DetectPythonFramework(ctx, files, deps);
                break;

            case "TypeScript/JavaScript":
                DetectNodeFramework(ctx, files, deps);
                break;
        }

        // Java modules can coexist with another primary repository language.
        // Classify them independently from module-owned build dependency evidence.
        DetectJavaFrameworks(ctx);

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
            var nodeDependencies = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            DetectNodeFramework(
                ctx,
                files,
                nodeDependencies,
                preservePrimaryFramework: true);
            deps.UnionWith(nodeDependencies);
            ctx.DetectedDependencies = deps.ToList();
        }

        var hasPackageManifest = files.Any(file =>
            Path.GetFileName(file).Equals(
                "package.json",
                StringComparison.OrdinalIgnoreCase));

        var hasUnclassifiedFrontendSource = files.Any(file =>
            Path.GetExtension(file).ToLowerInvariant() is ".tsx" or ".jsx" or ".vue");
        var isNodeLanguage = string.Equals(
            ctx.Language,
            "TypeScript/JavaScript",
            StringComparison.OrdinalIgnoreCase);

        if (!hasPackageManifest
            && (hasUnclassifiedFrontendSource || isNodeLanguage))
        {
            ctx.FrontendFrameworkDiscoveryComplete = false;
        }

        if (files.Any(file => Path.GetExtension(file).Equals(
                ".cshtml",
                StringComparison.OrdinalIgnoreCase)))
        {
            AddFramework(ctx, "Razor");
        }

        if (!string.IsNullOrWhiteSpace(ctx.Framework))
            AddFramework(ctx, ctx.Framework);

        // The repository-level framework is an aggregate. If its projects use
        // different frameworks, do not publish one project's framework as the
        // identity of the entire repository.
        foreach (var framework in ctx.ProductionModules.SelectMany(module => module.Frameworks))
            AddFramework(ctx, framework);

        // EF Core is a data-access capability on the .NET platform, not a
        // separate application framework. Normalize it for repository-wide
        // aggregation while retaining the precise module-level value.
        var nonTestModules = ctx.ProductionModules;

        var hostModules = nonTestModules
            .Where(module => module.ProjectKind is
                "WebApplication" or
                "FrontendApplication" or
                "DesktopApplication" or
                "Executable")
            .ToArray();

        var webModules = nonTestModules
            .Where(module => string.Equals(
                module.ProjectKind,
                "WebApplication",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // A split ASP.NET/Blazor web stack is one backend platform with a
        // frontend host, even when the repository also contains an embedded
        // Unity client. In contrast, a single Blazor web project beside an
        // independent Unity project is genuinely multi-framework.
        var isSplitBlazorWebStack = webModules.Length > 1
            && webModules.Any(module => module.Frameworks.Contains(
                "Blazor",
                StringComparer.OrdinalIgnoreCase));

        var hasOnlyHostsAndSupportingLibraries = nonTestModules.All(module =>
            hostModules.Contains(module)
            || string.Equals(
                module.ProjectKind,
                "ClassLibrary",
                StringComparison.OrdinalIgnoreCase));

        var frameworkModules = isSplitBlazorWebStack
            ? webModules
            : hostModules.Length > 0 && hasOnlyHostsAndSupportingLibraries
                ? hostModules
                : nonTestModules;

        var projectFrameworks = frameworkModules
            .Select(ResolveModulePrimaryFramework)
            .Where(framework => !string.IsNullOrWhiteSpace(framework))
            .Select(framework => string.Equals(
                framework,
                ".NET (EF Core)",
                StringComparison.OrdinalIgnoreCase)
                    ? ".NET"
                    : framework!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(framework => framework, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (projectFrameworks.Length > 1)
            ctx.Framework = "Multi-Framework";
        else if (projectFrameworks.Length == 1)
            ctx.Framework = projectFrameworks[0];
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

    private static string? ResolveModulePrimaryFramework(ProjectModuleContext module)
    {
        if (!string.Equals(
                module.Framework,
                "Multi-Framework",
                StringComparison.OrdinalIgnoreCase))
        {
            return module.Framework;
        }

        // A mixed module may expose a UI framework alongside its primary
        // runtime. Prefer the runtime for repository identity; individual
        // files retain their concrete framework through module.Frameworks.
        return module.Frameworks.FirstOrDefault(framework =>
                   string.Equals(framework, "ASP.NET Core", StringComparison.OrdinalIgnoreCase))
            ?? module.Frameworks.FirstOrDefault(framework =>
                   string.Equals(framework, ".NET (EF Core)", StringComparison.OrdinalIgnoreCase))
            ?? module.Frameworks.FirstOrDefault(framework =>
                   string.Equals(framework, ".NET", StringComparison.OrdinalIgnoreCase))
            ?? module.Frameworks.FirstOrDefault(framework =>
                   string.Equals(framework, "WPF", StringComparison.OrdinalIgnoreCase))
            ?? module.Frameworks.FirstOrDefault(framework =>
                   string.Equals(framework, "Windows Forms", StringComparison.OrdinalIgnoreCase))
            ?? "Multi-Framework";
    }

    private static void AddFramework(ProjectArchitectureContext context, string framework)
    {
        if (!context.DetectedFrameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
            context.DetectedFrameworks.Add(framework);
    }

    private static void AddFramework(ProjectModuleContext module, string framework)
    {
        if (!module.Frameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
            module.Frameworks.Add(framework);
    }

    private static string SafeReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return string.Empty; }
        catch (UnauthorizedAccessException) { return string.Empty; }
    }

    private static void DetectJavaFrameworks(ProjectArchitectureContext context)
    {
        foreach (var module in context.Modules.Where(module =>
                     module.BuildSystem is Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Maven
                         or Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Gradle
                     || module.Languages.Contains("Java", StringComparer.OrdinalIgnoreCase)))
        {
            var dependencies = context.DependencyEvidence.Where(dependency =>
                    PathsEqual(dependency.SourceProjectPath, module.Path)
                    && dependency.Ecosystem is "Maven" or "Gradle")
                .ToArray();

            var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in dependencies)
            {
                var separator = dependency.Name.IndexOf(':');
                if (separator <= 0 || separator == dependency.Name.Length - 1)
                    continue;

                var group = dependency.Name[..separator];
                var artifact = dependency.Name[(separator + 1)..];

                if (group.Equals("org.springframework.boot", StringComparison.OrdinalIgnoreCase)
                    && artifact.StartsWith("spring-boot", StringComparison.OrdinalIgnoreCase))
                {
                    frameworks.Add("Spring Boot");
                }
                else if (group.Equals("org.springframework", StringComparison.OrdinalIgnoreCase)
                         && artifact.StartsWith("spring-", StringComparison.OrdinalIgnoreCase))
                {
                    frameworks.Add("Spring Framework");
                }
                else if (group.Equals("io.quarkus", StringComparison.OrdinalIgnoreCase))
                {
                    frameworks.Add("Quarkus");
                }
                else if (group.Equals("io.micronaut", StringComparison.OrdinalIgnoreCase))
                {
                    frameworks.Add("Micronaut");
                }
                else if ((group.Equals("org.hibernate", StringComparison.OrdinalIgnoreCase)
                          || group.Equals("org.hibernate.orm", StringComparison.OrdinalIgnoreCase))
                         && artifact.StartsWith("hibernate-", StringComparison.OrdinalIgnoreCase))
                {
                    frameworks.Add("Hibernate");
                }
                else if (group.StartsWith("jakarta.", StringComparison.OrdinalIgnoreCase)
                         || group.Equals("jakarta", StringComparison.OrdinalIgnoreCase))
                {
                    frameworks.Add("Jakarta EE");
                }
            }

            foreach (var framework in frameworks)
            {
                AddFramework(module, framework);
                AddFramework(context, framework);
            }

            // Keep all detected framework signals on the module, while selecting
            // a deterministic primary identity for repository aggregation.
            module.Framework = frameworks
                .OrderBy(GetJavaFrameworkPriority)
                .ThenBy(framework => framework, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? "Java SE";
        }

        if (string.Equals(context.Language, "Java", StringComparison.OrdinalIgnoreCase)
            && !context.Modules.Any(module =>
                module.BuildSystem is Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Maven
                    or Aegis.Shared.Architecture.Enums.ArchitectureBuildSystem.Gradle
                || module.Languages.Contains("Java", StringComparer.OrdinalIgnoreCase)))
        {
            context.Framework ??= "Java SE";
        }
    }

    private static int GetJavaFrameworkPriority(string framework) => framework switch
    {
        "Spring Boot" => 0,
        "Quarkus" => 1,
        "Micronaut" => 2,
        "Jakarta EE" => 3,
        "Spring Framework" => 4,
        "Hibernate" => 5,
        _ => int.MaxValue
    };

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

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
                foreach (var key in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
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
                    or UnauthorizedAccessException
                    or InvalidOperationException)
            {
                ctx.FrontendFrameworkDiscoveryComplete = false;

                // A malformed or unreadable package manifest cannot establish
                // that a frontend framework is absent. Continue with readable
                // manifests while retaining the incomplete-discovery state.
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
