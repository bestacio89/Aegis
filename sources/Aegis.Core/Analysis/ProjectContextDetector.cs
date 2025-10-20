using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Aegis.Shared.Enums;
using Aegis.Shared.Models;

namespace Aegis.Core.Analysis;

/// <summary>
/// 🔍 Detects and infers the structural, architectural, and technological
/// profile of a project deterministically, prior to evaluation.
/// </summary>
public static class ProjectContextDetector
{
    public static ProjectContext Detect(string rootPath)
    {
        var now = DateTime.UtcNow;

        // Initialize detection context
        var ctx = new ProjectContext
        {
            DetectedAt = now,
            DetectorVersion = "1.3.0",
            DetectionStrategy = "Hybrid",
            RootPath = rootPath
        };

        // ────────────────────────────────────────────────────────────────
        // Collect project files (excluding build/cache/system folders)
        // ────────────────────────────────────────────────────────────────
        var files = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                             .Where(f => !IsExcluded(f))
                             .ToList();

        ctx.FileCount = files.Count;
        ctx.IsMultiModule = files.Count(f =>
            f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith("package.json", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase)) > 1;

        // 1️⃣ Detect language & build system
        DetectLanguageAndBuildSystem(files, ctx);

        // 2️⃣ Detect frameworks & dependencies
        var dependencies = DetectFrameworksAndDependencies(files, ctx);
        ctx.DetectedDependencies.AddRange(dependencies);

        // 3️⃣ Determine domain type & backend layer
        DetectDomainAndLayer(files, ctx);

        // 4️⃣ Detect deployability (Docker/Kubernetes)
        DetectDeployability(files, ctx);

        // 5️⃣ Detect nature (Service, SDK, Library, TestSuite)
        DetectNature(files, ctx);

        // 6️⃣ Infer architecture style
        DetectArchitectureStyle(ctx);

        // 7️⃣ Guess entry point file
        ctx.EntryPointFile = GuessEntryPoint(files, ctx);

        // 8️⃣ Compute global confidence score
        ComputeConfidence(ctx);

        // 9️⃣ Generate static ProjectMetadata (immutable)
        var projectName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var frameworkVersion = ctx.TargetRuntime ?? ctx.DetectedFrameworks.FirstOrDefault() ?? "N/A";
        var lastModified = files.Select(f => File.GetLastWriteTimeUtc(f)).DefaultIfEmpty(now).Max();

        ctx.Metadata = new ProjectMetadata(
            Name: projectName,
            Path: rootPath,
            Language: ctx.Language,
            Framework: ctx.Framework ?? "Unknown",
            Version: frameworkVersion,
            LastModified: lastModified
        );

        return ctx;
    }

    // ────────────────────────────────────────────────────────────────
    // 🔧 Detection Logic — ordered by inference dependency
    // ────────────────────────────────────────────────────────────────

    private static void DetectLanguageAndBuildSystem(List<string> files, ProjectContext ctx)
    {
        bool hasCs = files.Any(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        bool hasTs = files.Any(f => f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase));
        bool hasJs = files.Any(f => f.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
        bool hasPy = files.Any(f => f.EndsWith(".py", StringComparison.OrdinalIgnoreCase));
        bool hasJava = files.Any(f => f.EndsWith(".java", StringComparison.OrdinalIgnoreCase));

        ctx.Language = hasCs ? "C#" :
                       hasTs ? "TypeScript" :
                       hasJs ? "JavaScript" :
                       hasJava ? "Java" :
                       hasPy ? "Python" : "Unknown";

        if (files.Any(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)))
            ctx.BuildSystem = BuildSystem.DotNet;
        else if (files.Any(f => Path.GetFileName(f).Equals("pom.xml", StringComparison.OrdinalIgnoreCase)))
            ctx.BuildSystem = BuildSystem.Maven;
        else if (files.Any(f => f.EndsWith(".gradle", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".gradle.kts", StringComparison.OrdinalIgnoreCase)))
            ctx.BuildSystem = BuildSystem.Gradle;
        else if (files.Any(f => Path.GetFileName(f).Equals("package.json", StringComparison.OrdinalIgnoreCase)))
        {
            ctx.BuildSystem = files.Any(f => Path.GetFileName(f).Equals("yarn.lock", StringComparison.OrdinalIgnoreCase))
                ? BuildSystem.Yarn
                : files.Any(f => Path.GetFileName(f).Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase))
                    ? BuildSystem.Pnpm
                    : BuildSystem.Npm;
        }
        else ctx.BuildSystem = BuildSystem.Unknown;
    }

    private static IEnumerable<string> DetectFrameworksAndDependencies(List<string> files, ProjectContext ctx)
    {
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 🟦 .NET projects
        foreach (var proj in files.Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var xml = XDocument.Load(proj);
                var tfm = xml.Descendants("TargetFramework").Select(e => e.Value).FirstOrDefault()
                         ?? xml.Descendants("TargetFrameworks").Select(e => e.Value).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(tfm))
                {
                    ctx.Framework = ".NET " + tfm;
                    ctx.TargetRuntime = tfm;
                    ctx.DetectedFrameworks.Add(tfm);
                }

                foreach (var pkg in xml.Descendants("PackageReference"))
                {
                    var name = (string?)pkg.Attribute("Include") ?? (string?)pkg.Attribute("Update");
                    if (!string.IsNullOrWhiteSpace(name))
                        deps.Add(name!);
                }
            }
            catch { /* ignore parse errors */ }
        }

        // 🟨 Node.js / TypeScript
        foreach (var pkg in files.Where(f => Path.GetFileName(f).Equals("package.json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var json = JsonNode.Parse(File.ReadAllText(pkg))?.AsObject();
                if (json == null) continue;

                foreach (var set in new[] { "dependencies", "devDependencies" })
                {
                    if (json[set] is JsonObject obj)
                        foreach (var kv in obj)
                            deps.Add(kv.Key);
                }

                if (deps.Contains("@angular/core")) ctx.Framework = "Angular";
                else if (deps.Contains("react")) ctx.Framework = "React";
                else if (deps.Contains("vue")) ctx.Framework = "Vue";
            }
            catch { /* ignore */ }
        }

        // 🟩 Java / Maven
        foreach (var pom in files.Where(f => Path.GetFileName(f).Equals("pom.xml", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var xml = XDocument.Load(pom);
                var spring = xml.Descendants().Any(e =>
                    e.Name.LocalName == "groupId" && e.Value.Contains("org.springframework", StringComparison.OrdinalIgnoreCase));
                if (spring)
                    ctx.Framework = "Spring Boot";

                foreach (var dep in xml.Descendants().Where(e => e.Name.LocalName == "dependency"))
                {
                    var gid = dep.Elements().FirstOrDefault(e => e.Name.LocalName == "groupId")?.Value;
                    var aid = dep.Elements().FirstOrDefault(e => e.Name.LocalName == "artifactId")?.Value;
                    if (!string.IsNullOrWhiteSpace(aid))
                        deps.Add($"{gid}:{aid}");
                }
            }
            catch { /* ignore */ }
        }

        return deps;
    }

    private static void DetectDomainAndLayer(List<string> files, ProjectContext ctx)
    {
        bool angular = files.Any(f => Path.GetFileName(f).Equals("angular.json", StringComparison.OrdinalIgnoreCase));
        bool react = ctx.Framework == "React" || files.Any(f => f.EndsWith("next.config.js", StringComparison.OrdinalIgnoreCase));
        bool vue = ctx.Framework == "Vue";

        if (angular || react || vue)
        {
            ctx.DomainType = "Frontend";
            ctx.Layer = "Unknown";
            return;
        }

        // Backend inference (.NET, Java, etc.)
        if (ctx.BuildSystem is BuildSystem.DotNet or BuildSystem.Maven or BuildSystem.Gradle)
        {
            ctx.DomainType = "Backend";

            var dirs = files.Select(f => Path.GetDirectoryName(f) ?? "")
                            .Distinct()
                            .Select(d => Path.GetFileName(d)?.ToLowerInvariant() ?? "")
                            .ToList();

            if (dirs.Any(d => d.Contains("api") || d.Contains("controller"))) ctx.Layer = "Api";
            else if (dirs.Any(d => d.Contains("application") || d.Contains("service"))) ctx.Layer = "Application";
            else if (dirs.Any(d => d.Contains("domain") || d.Contains("core"))) ctx.Layer = "Domain";
            else if (dirs.Any(d => d.Contains("infra") || d.Contains("repository"))) ctx.Layer = "Infrastructure";
            else ctx.Layer = "Unknown";

            return;
        }

        // Library / Infra fallback
        if (files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}")))
            ctx.DomainType = "Library";
        else if (files.Any(IsIaCFile))
            ctx.DomainType = "Infra";
        else
            ctx.DomainType = "Unknown";
    }

    private static bool IsIaCFile(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name is "dockerfile" or "docker-compose.yml" or "docker-compose.yaml"
            || name.EndsWith(".bicep") || name.EndsWith(".tf")
            || (name.EndsWith(".yaml") || name.EndsWith(".yml")) &&
               (name.Contains("deployment") || name.Contains("service") || name.Contains("ingress"));
    }

    private static void DetectDeployability(List<string> files, ProjectContext ctx)
    {
        bool dockerfile = files.Any(f => Path.GetFileName(f).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase));
        bool compose = files.Any(f => Path.GetFileName(f).StartsWith("docker-compose", StringComparison.OrdinalIgnoreCase));
        bool k8s = files.Any(f => Regex.IsMatch(Path.GetFileName(f), @"(deployment|statefulset|daemonset|service|ingress)\.ya?ml", RegexOptions.IgnoreCase));

        if (dockerfile || compose) ctx.MetadataMap["Dockerized"] = "true";
        if (k8s) ctx.MetadataMap["Kubernetes"] = "true";

        if (ctx.IsDeployable)
            ctx.Nature ??= "Service";
    }

    private static void DetectNature(List<string> files, ProjectContext ctx)
    {
        bool tests = files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        bool sdk = files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}sdk{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)) ||
                   ctx.DetectedDependencies.Any(d => d.Contains("Sdk", StringComparison.OrdinalIgnoreCase));
        bool lib = files.Any(f => f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        if (tests) ctx.Nature = "TestSuite";
        else if (sdk) ctx.Nature = "SDK";
        else if (lib) ctx.Nature = "Library";
        else if (ctx.DomainType == "Backend") ctx.Nature = "Service";
        else if (ctx.DomainType == "Frontend") ctx.Nature = "FrontendApp";
        else ctx.Nature ??= "Generic";
    }

    private static void DetectArchitectureStyle(ProjectContext ctx)
    {
        bool hasMediatR = ctx.DetectedDependencies.Any(d => d.Equals("MediatR", StringComparison.OrdinalIgnoreCase));
        bool hasEfCore = ctx.DetectedDependencies.Any(d => d.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        bool hasMassTr = ctx.DetectedDependencies.Any(d => d.StartsWith("MassTransit", StringComparison.OrdinalIgnoreCase));
        bool hasKafka = ctx.DetectedDependencies.Any(d => d.Contains("Confluent.Kafka", StringComparison.OrdinalIgnoreCase));

        if (hasMediatR && hasEfCore) ctx.ArchitectureStyle = "CQRS";
        else if (hasEfCore && ctx.Layer?.Equals("Domain", StringComparison.OrdinalIgnoreCase) == true) ctx.ArchitectureStyle = "Clean";
        else if (hasMassTr || hasKafka) ctx.ArchitectureStyle = "Hexagonal";
        else if (ctx.IsMultiModule && ctx.Nature == "Service") ctx.ArchitectureStyle = "ModularMonolith";
        else if (ctx.DomainType == "Backend" && ctx.Layer == "Api") ctx.ArchitectureStyle = "MVC";
        else ctx.ArchitectureStyle = "Layered";
    }

    private static string? GuessEntryPoint(List<string> files, ProjectContext ctx)
    {
        if (ctx.BuildSystem == BuildSystem.DotNet)
        {
            var program = files.FirstOrDefault(f => Path.GetFileName(f).Equals("Program.cs", StringComparison.OrdinalIgnoreCase));
            if (program != null) return program;
        }

        if (ctx.BuildSystem is BuildSystem.Npm or BuildSystem.Yarn or BuildSystem.Pnpm)
        {
            var pkg = files.FirstOrDefault(f => Path.GetFileName(f).Equals("package.json", StringComparison.OrdinalIgnoreCase));
            if (pkg != null)
            {
                try
                {
                    var json = JsonNode.Parse(File.ReadAllText(pkg))?.AsObject();
                    var main = json?["main"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(main))
                        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pkg)!, main));
                }
                catch { /* ignore */ }
            }
        }

        return null;
    }

    private static void ComputeConfidence(ProjectContext ctx)
    {
        ctx.ConfidenceMap["Language"] = ctx.Language != "Unknown" ? 1.0 : 0.4;
        ctx.ConfidenceMap["Framework"] = !string.IsNullOrEmpty(ctx.Framework) ? 0.9 : 0.5;
        ctx.ConfidenceMap["BuildSystem"] = ctx.BuildSystem != BuildSystem.Unknown ? 0.9 : 0.5;
        ctx.ConfidenceMap["DomainType"] = ctx.DomainType != "Unknown" ? 0.85 : 0.5;
        ctx.ConfidenceMap["Layer"] = ctx.Layer != "Unknown" ? 0.8 : 0.4;
        ctx.ConfidenceMap["ArchitectureStyle"] = ctx.ArchitectureStyle != "Unknown" ? 0.75 : 0.5;
        ctx.ConfidenceMap["Deployability"] = ctx.IsDeployable ? 0.9 : 0.5;

        ctx.Confidence = Math.Clamp(ctx.ConfidenceMap.Values.DefaultIfEmpty(0.6).Average(), 0, 1);
    }

    private static bool IsExcluded(string path)
    {
        var p = path.ToLowerInvariant();
        return p.Contains("\\bin\\") ||
               p.Contains("\\obj\\") ||
               p.Contains("\\node_modules\\") ||
               p.Contains("\\.git\\") ||
               p.Contains("\\.vs\\") ||
               p.Contains("\\.idea\\");
    }
}
