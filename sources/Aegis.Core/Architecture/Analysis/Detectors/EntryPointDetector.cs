using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Core.Architecture.Analysis.Detectors;

/// <summary>
/// 🧩 Detects the program's entry point (main file or class).
/// Supports .NET, Node.js, Python, and Java ecosystems.
/// </summary>
public static class EntryPointDetector
{
    public static void Analyze(ProjectArchitectureContext ctx, List<string> files)
    {
        // Default value
        ctx.EntryPointFile = null;

        try
        {
            switch (ctx.BuildSystem)
            {
                case ArchitectureBuildSystem.DotNet:
                    DetectDotNetEntryPoint(ctx, files);
                    break;

                case ArchitectureBuildSystem.Npm:
                case ArchitectureBuildSystem.Yarn:
                case ArchitectureBuildSystem.Pnpm:
                    DetectNodeEntryPoint(ctx, files);
                    break;

                case ArchitectureBuildSystem.Maven:
                case ArchitectureBuildSystem.Gradle:
                    DetectJavaEntryPoint(ctx, files);
                    break;

                default:
                    if (ctx.Language == "Python")
                        DetectPythonEntryPoint(ctx, files);
                    break;
            }

            // Log fallback result
            ctx.EntryPointFile ??= files.FirstOrDefault(f =>
                f.EndsWith("main.cs", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("main.py", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("main.ts", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("main.js", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            ctx.MetadataMap["EntryPointError"] = ex.Message;
        }
    }

    // ─────────────────────────────
    // 🟦 .NET Entry Point Detector
    // ─────────────────────────────
    private static void DetectDotNetEntryPoint(ProjectArchitectureContext ctx, List<string> files)
    {
        var program = files.FirstOrDefault(f =>
            Path.GetFileName(f).Equals("Program.cs", StringComparison.OrdinalIgnoreCase));

        if (program is not null)
        {
            ctx.EntryPointFile = program;
            return;
        }

        // Detect minimal API or startup files
        var startup = files.FirstOrDefault(f =>
            Path.GetFileName(f).Equals("Startup.cs", StringComparison.OrdinalIgnoreCase));

        if (startup is not null)
        {
            ctx.EntryPointFile = startup;
        }
    }

    // ─────────────────────────────
    // 🟨 Node.js / TS Entry Point
    // ─────────────────────────────
    private static void DetectNodeEntryPoint(ProjectArchitectureContext ctx, List<string> files)
    {
        var pkg = files.FirstOrDefault(f =>
            Path.GetFileName(f).Equals("package.json", StringComparison.OrdinalIgnoreCase));
        if (pkg == null)
            return;

        try
        {
            var json = JsonNode.Parse(File.ReadAllText(pkg))?.AsObject();
            var main = json?["main"]?.GetValue<string>()
                       ?? json?["scripts"]?["start"]?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(main))
            {
                var entry = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pkg)!, main));
                ctx.EntryPointFile = entry;
            }
        }
        catch
        {
            // fallback: look for index.js or app.js
            ctx.EntryPointFile = files.FirstOrDefault(f =>
                Path.GetFileName(f).Equals("index.js", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).Equals("app.js", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).Equals("server.js", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ─────────────────────────────
    // 🐍 Python Entry Point Detector
    // ─────────────────────────────
    private static void DetectPythonEntryPoint(ProjectArchitectureContext ctx, List<string> files)
    {
        var main = files.FirstOrDefault(f =>
            Path.GetFileName(f).Equals("main.py", StringComparison.OrdinalIgnoreCase));
        if (main is not null)
        {
            ctx.EntryPointFile = main;
            return;
        }

        // Look for file containing "__main__"
        foreach (var f in files.Where(f => f.EndsWith(".py")))
        {
            try
            {
                var text = File.ReadAllText(f);
                if (text.Contains("__main__"))
                {
                    ctx.EntryPointFile = f;
                    break;
                }
            }
            catch { }
        }
    }

    // ─────────────────────────────
    // ☕ Java Entry Point Detector
    // ─────────────────────────────
    private static void DetectJavaEntryPoint(ProjectArchitectureContext ctx, List<string> files)
    {
        foreach (var javaFile in files.Where(f => f.EndsWith(".java")))
        {
            try
            {
                var content = File.ReadAllText(javaFile);
                if (Regex.IsMatch(content, @"public\s+static\s+void\s+main\s*\(", RegexOptions.IgnoreCase))
                {
                    ctx.EntryPointFile = javaFile;
                    break;
                }
            }
            catch { }
        }
    }
}
