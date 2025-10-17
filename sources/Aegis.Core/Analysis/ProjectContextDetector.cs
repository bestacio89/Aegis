using Aegis.Core.Diagnostics;
using Aegis.Shared.Diagnostics;
using Aegis.Shared.Enums;
using Aegis.Shared.Models;
using Franz.Common.Errors;
using System.IO;
using System.Text.Json;
using Aegis.Shared.Utilities;

namespace Aegis.Core.Analysis;

/// <summary>
/// Automatically detects the language, framework, and architectural layer
/// of a given project path, caching results to accelerate future scans.
/// </summary>
public static class ProjectContextDetector
{
    private const string CacheFile = "aegis.context.cache.json";
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aegis",
        CacheFile
    );

    private static readonly Dictionary<string, ProjectContext> _cache = LoadCache();

    public static ProjectContext Detect(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            throw new TechnicalException($"Invalid project path '{projectPath}'.");

        // 🔁 Step 1: Check cached context
        if (_cache.TryGetValue(projectPath, out var cached))
        {
            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Loaded cached context for {projectPath}");
            return cached;
        }

        // 🧩 Step 2: Inspect repository
        var files = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories);

        var context =
            DetectDotNet(files) ??
            DetectJavaScript(files) ??
            DetectJava(files) ??
            DetectPython(files) ??
            new ProjectContext("Unknown", null, 0.0)
            {
                Layer = DetectLayer(projectPath),
                Nature = DetectNature(projectPath)
            };

        // 💾 Step 3: Cache result
        context.Layer ??= DetectLayer(projectPath);
        context.Nature ??= DetectNature(projectPath);

        _cache[projectPath] = context;
        SaveCache();

        AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Info,
            $"Detected {context.Language}/{context.Framework} → Layer: {context.Layer} Nature: {context.Nature}");

        return context;
    }

    // =======================================================================
    // 🟦 .NET / C# DETECTION
    // =======================================================================
    private static ProjectContext? DetectDotNet(string[] files)
    {
        if (!files.Any(f => f.EndsWith(".csproj") || f.EndsWith(".sln")))
            return null;

        string framework = "Generic .NET";
        double confidence = 0.9;

        if (files.Any(f => f.Contains("MAUI", StringComparison.OrdinalIgnoreCase))) framework = "MAUI";
        else if (files.Any(f => f.Contains("WPF", StringComparison.OrdinalIgnoreCase))) framework = "WPF";
        else if (files.Any(f => f.Contains("AspNet", StringComparison.OrdinalIgnoreCase))) framework = "ASP.NET";
        else if (files.Any(f => f.Contains("Blazor", StringComparison.OrdinalIgnoreCase))) framework = "Blazor";
        else if (files.Any(f => f.Contains("Unity", StringComparison.OrdinalIgnoreCase))) framework = "Unity";

        return new ProjectContext("CSharp", framework, confidence);
    }

    // =======================================================================
    // 🟨 JAVASCRIPT / TYPESCRIPT DETECTION
    // =======================================================================
    private static ProjectContext? DetectJavaScript(string[] files)
    {
        if (!files.Any(f => f.EndsWith("package.json")))
            return null;

        string framework = "Node";
        double confidence = 0.9;

        if (files.Any(f => f.Contains("angular.json"))) framework = "Angular";
        else if (files.Any(f => f.Contains("react", StringComparison.OrdinalIgnoreCase))) framework = "React";
        else if (files.Any(f => f.Contains("vue", StringComparison.OrdinalIgnoreCase))) framework = "Vue";
        else if (files.Any(f => f.Contains("next.config", StringComparison.OrdinalIgnoreCase))) framework = "Next.js";
        else if (files.Any(f => f.Contains("nuxt", StringComparison.OrdinalIgnoreCase))) framework = "Nuxt.js";
        else if (files.Any(f => f.Contains("nest-cli.json"))) framework = "NestJS";

        return new ProjectContext("TypeScript", framework, confidence);
    }

    // =======================================================================
    // ☕ JAVA DETECTION
    // =======================================================================
    private static ProjectContext? DetectJava(string[] files)
    {
        if (!files.Any(f => f.EndsWith(".java") || f.EndsWith("pom.xml") || f.EndsWith("build.gradle")))
            return null;

        string framework = "Generic Java";
        double confidence = 0.85;

        if (files.Any(f => f.EndsWith("pom.xml")))
        {
            framework = "Spring Boot";
            confidence = 0.95;
        }
        else if (files.Any(f => f.EndsWith("build.gradle") || f.EndsWith("build.gradle.kts")))
        {
            framework = "Gradle Project";
            confidence = 0.9;
        }

        if (files.Any(f => f.Contains("quarkus", StringComparison.OrdinalIgnoreCase))) framework = "Quarkus";
        else if (files.Any(f => f.Contains("micronaut", StringComparison.OrdinalIgnoreCase))) framework = "Micronaut";
        else if (files.Any(f => f.Contains("jakarta", StringComparison.OrdinalIgnoreCase))) framework = "Jakarta EE";
        else if (files.Any(f => f.Contains("playframework", StringComparison.OrdinalIgnoreCase))) framework = "Play Framework";

        return new ProjectContext("Java", framework, confidence);
    }

    // =======================================================================
    // 🐍 PYTHON DETECTION
    // =======================================================================
    private static ProjectContext? DetectPython(string[] files)
    {
        if (!files.Any(f => f.EndsWith(".py") || f.EndsWith("requirements.txt") || f.EndsWith("pyproject.toml")))
            return null;

        string framework = "Generic Python";
        double confidence = 0.8;

        if (files.Any(f => f.Contains("fastapi", StringComparison.OrdinalIgnoreCase)))
        {
            framework = "FastAPI";
            confidence = 0.9;
        }
        else if (files.Any(f => f.Contains("flask", StringComparison.OrdinalIgnoreCase)))
        {
            framework = "Flask";
            confidence = 0.9;
        }
        else if (files.Any(f => f.Contains("manage.py") || f.Contains("django", StringComparison.OrdinalIgnoreCase)))
        {
            framework = "Django";
            confidence = 0.95;
        }
        else if (files.Any(f => f.Contains("pyramid", StringComparison.OrdinalIgnoreCase))) framework = "Pyramid";
        else if (files.Any(f => f.Contains("tornado", StringComparison.OrdinalIgnoreCase))) framework = "Tornado";
        else if (files.Any(f => f.Contains("falcon", StringComparison.OrdinalIgnoreCase))) framework = "Falcon";

        return new ProjectContext("Python", framework, confidence);
    }

    // =======================================================================
    // 🧱 LAYER & NATURE DETECTION
    // =======================================================================
    private static string DetectLayer(string projectPath)
    {
        try
        {
            // Gather a small, representative sample of files (avoid reading huge files unnecessarily)
            var allFiles = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
                                    .Where(f => !PathUtils.IsExcludedDir(Path.GetDirectoryName(f) ?? string.Empty))
                                    .ToList();

            // 1) Quick folder-name heuristics (fast path)
            var folderName = Path.GetFileName(projectPath)?.ToLowerInvariant() ?? "";
            if (folderName.Contains("domain")) return "Domain";
            if (folderName.Contains("application") || folderName.Contains("app")) return "Application";
            if (folderName.Contains("infrastructure") || folderName.Contains("infra")) return "Infrastructure";
            if (folderName.Contains("api") || folderName.Contains("presentation") || folderName.Contains("web")) return "Api";

            // 2) Inspect project files for explicit web SDK / controller markers (C#)
            var csproj = allFiles.FirstOrDefault(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
            if (csproj != null)
            {
                try
                {
                    var csprojText = File.ReadAllText(csproj);
                    if (csprojText.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) ||
                        csprojText.Contains("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
                    {
                        AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, "csproj indicates Web SDK -> Api layer");
                        return "Api";
                    }

                    // If project references only domain-level libs (heuristic)
                    if (csprojText.Contains("ProjectReference") && csprojText.Contains("Domain", StringComparison.OrdinalIgnoreCase))
                    {
                        AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, "csproj references Domain -> Application/Domain");
                        return "Application";
                    }
                }
                catch { /* non-fatal: continue heuristics */ }
            }

            // 3) C# heuristics: Controllers, ApiController attributes, MapControllers, AddControllers
            var csharpSample = allFiles.FirstOrDefault(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
            if (csharpSample != null)
            {
                try
                {
                    // read a small set of C# files (to avoid huge IO)
                    var csFiles = allFiles.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                                          .Take(40)
                                          .ToList();

                    foreach (var f in csFiles)
                    {
                        var t = File.ReadAllText(f);
                        if (t.Contains("[ApiController]", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("class ") && t.Contains("Controller", StringComparison.Ordinal))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected Controller in {f} -> Api layer");
                            return "Api";
                        }

                        if (t.Contains("namespace", StringComparison.OrdinalIgnoreCase) && t.Contains(".Domain", StringComparison.OrdinalIgnoreCase))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected Domain namespace in {f} -> Domain layer");
                            return "Domain";
                        }

                        if (t.Contains("DbContext", StringComparison.OrdinalIgnoreCase) || t.Contains("Repository", StringComparison.OrdinalIgnoreCase))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected persistence references in {f} -> Infrastructure layer");
                            return "Infrastructure";
                        }
                    }
                }
                catch { /* ignore and continue */ }
            }

            // 4) Java heuristics
            var javaFile = allFiles.FirstOrDefault(f => f.EndsWith(".java", StringComparison.OrdinalIgnoreCase));
            if (javaFile != null)
            {
                try
                {
                    var javaFiles = allFiles.Where(f => f.EndsWith(".java", StringComparison.OrdinalIgnoreCase)).Take(60);
                    foreach (var f in javaFiles)
                    {
                        var t = File.ReadAllText(f);
                        if (t.Contains("@RestController") || t.Contains("Controller", StringComparison.OrdinalIgnoreCase) || t.Contains("RequestMapping"))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected Java Controller in {f} -> Api layer");
                            return "Api";
                        }
                        if (t.Contains("@Service") || t.Contains("Service", StringComparison.OrdinalIgnoreCase))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected Java Service in {f} -> Application layer");
                            return "Application";
                        }
                        if (t.Contains(".domain.") || t.Contains("Entity", StringComparison.OrdinalIgnoreCase))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected Java domain types in {f} -> Domain layer");
                            return "Domain";
                        }
                    }
                }
                catch { /* continue */ }
            }

            // 5) Python heuristics
            var pyFile = allFiles.FirstOrDefault(f => f.EndsWith(".py", StringComparison.OrdinalIgnoreCase));
            if (pyFile != null)
            {
                try
                {
                    var pyFiles = allFiles.Where(f => f.EndsWith(".py", StringComparison.OrdinalIgnoreCase)).Take(80);
                    foreach (var f in pyFiles)
                    {
                        var t = File.ReadAllText(f);
                        if (t.Contains("from fastapi import FastAPI") || t.Contains("FastAPI("))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected FastAPI in {f} -> Api layer");
                            return "Api";
                        }
                        if (t.Contains("manage.py") || t.Contains("django", StringComparison.OrdinalIgnoreCase))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected Django in {f} -> Api layer");
                            return "Api";
                        }
                        if (f.EndsWith("models.py", StringComparison.OrdinalIgnoreCase) || t.Contains("class ") && t.Contains("Model"))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected models in {f} -> Domain layer");
                            return "Domain";
                        }
                        if (t.Contains("session") && t.Contains("commit"))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected DB transaction in {f} -> Infrastructure layer");
                            return "Infrastructure";
                        }
                    }
                }
                catch { /* continue */ }
            }

            // 6) JS/TS heuristics (Express / Next / Nest / Angular)
            var jsFile = allFiles.FirstOrDefault(f => f.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase));
            if (jsFile != null)
            {
                try
                {
                    var jsFiles = allFiles.Where(f => f.EndsWith(".js") || f.EndsWith(".ts")).Take(80);
                    foreach (var f in jsFiles)
                    {
                        var t = File.ReadAllText(f);
                        if (t.Contains("express()") || t.Contains("app.use(") || t.Contains("router.") || t.Contains("app.get("))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected Express/Router in {f} -> Api layer");
                            return "Api";
                        }
                        if (t.Contains("pages/api") || f.Contains("api/") || t.Contains("next()") || f.Contains("pages"))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected Next/Pages API in {f} -> Api layer");
                            return "Api";
                        }
                        if (t.Contains("@Controller") || t.Contains("NestFactory") || t.Contains("Module"))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected NestJS/controller in {f} -> Api layer");
                            return "Api";
                        }
                        if (t.Contains("class ") && t.Contains("Strategy") || t.Contains("Strategy", StringComparison.OrdinalIgnoreCase))
                        {
                            // Could be domain or application — don't decide prematurely
                        }
                        if (t.Contains("Repository") || t.Contains("DbClient") || t.Contains("prisma"))
                        {
                            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Debug, $"Detected persistence in {f} -> Infrastructure layer");
                            return "Infrastructure";
                        }
                    }
                }
                catch { /* continue */ }
            }

            // 7) Fallback by file-count heuristics (project size)
            var fileCount = allFiles.Count;
            if (fileCount > 800) return "Platform";
            if (fileCount > 300) return "Infrastructure";
            if (fileCount > 100) return "Application";

            // final fallback
            return "Domain";
        }
        catch (Exception ex)
        {
            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Error, "Layer detection failed, falling back to Domain.", ex);
            return "Domain";
        }
    }

    private static string DetectNature(string projectPath)
    {
        var name = Path.GetFileName(projectPath).ToLowerInvariant();

        if (name.Contains("framework")) return "Framework";
        if (name.Contains("sdk")) return "SDK";
        if (name.Contains("service")) return "Service";
        if (name.Contains("micro")) return "Microservice";
        if (name.Contains("api")) return "API";
        return "Generic";
    }

    // =======================================================================
    // 💾 CACHE HANDLING
    // =======================================================================
    private static Dictionary<string, ProjectContext> LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return new();

            var json = File.ReadAllText(CachePath);
            return JsonSerializer.Deserialize<Dictionary<string, ProjectContext>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(
                CachePath,
                JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true })
            );
        }
        catch (Exception ex)
        {
            AegisDiagnostics.Report("ContextDetector", DiagnosticLevel.Warning,
                "Failed to persist project context cache.", ex);
        }
    }
}
