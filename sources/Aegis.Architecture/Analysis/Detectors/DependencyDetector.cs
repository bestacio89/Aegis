using System.Text.Json;
using System.Text.RegularExpressions;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis.Detectors;

internal static class DependencyDetector
{
    public static void Analyze(
        ProjectArchitectureContext ctx,
        List<string> files)
    {
        var dependencies = new HashSet<string>(
            ctx.DetectedDependencies,
            StringComparer.OrdinalIgnoreCase);


        switch (ctx.BuildSystem)
        {
            case ArchitectureBuildSystem.DotNet:
                AnalyzeDotNet(ctx, files, dependencies);
                break;


            case ArchitectureBuildSystem.Npm:
            case ArchitectureBuildSystem.Yarn:
            case ArchitectureBuildSystem.Pnpm:
                AnalyzeNode(ctx, files, dependencies);
                break;


            case ArchitectureBuildSystem.Maven:
            case ArchitectureBuildSystem.Gradle:
                AnalyzeJava(ctx, files, dependencies);
                break;
        }


        ctx.DetectedDependencies =
            dependencies.ToList();
    }



    private static void AnalyzeDotNet(
        ProjectArchitectureContext ctx,
        List<string> files,
        HashSet<string> dependencies)
    {
        foreach (var file in files.Where(
                     x => x.EndsWith(".csproj",
                         StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var xml = File.ReadAllText(file);


                foreach (Match match in Regex.Matches(
                             xml,
                             @"PackageReference\s+Include=""([^""]+)"""))
                {
                    if (match.Groups.Count <= 1)
                        continue;


                    var dependency =
                        match.Groups[1].Value;


                    dependencies.Add(dependency);


                    ctx.Dependencies.Add(
                        CreateDependency(
                            source: Path.GetFileNameWithoutExtension(file),
                            target: dependency,
                            type: "NuGet",
                            file));
                }
            }
            catch
            {
                // Ignore unreadable project files
            }
        }
    }



    private static void AnalyzeNode(
        ProjectArchitectureContext ctx,
        List<string> files,
        HashSet<string> dependencies)
    {
        var packageFile =
            files.FirstOrDefault(
                x => Path.GetFileName(x)
                    .Equals(
                        "package.json",
                        StringComparison.OrdinalIgnoreCase));


        if (packageFile is null)
            return;


        try
        {
            using var document =
                JsonDocument.Parse(
                    File.ReadAllText(packageFile));


            foreach (var section in new[]
                     {
                         "dependencies",
                         "devDependencies",
                         "peerDependencies"
                     })
            {
                if (!document.RootElement.TryGetProperty(
                        section,
                        out var dependencyNode))
                {
                    continue;
                }


                foreach (var dependency in dependencyNode.EnumerateObject())
                {
                    dependencies.Add(dependency.Name);


                    ctx.Dependencies.Add(
                        CreateDependency(
                            source: Path.GetFileNameWithoutExtension(packageFile),
                            target: dependency.Name,
                            type: "NPM",
                            packageFile));
                }
            }
        }
        catch
        {
            // Invalid package.json should not break analysis
        }
    }



    private static void AnalyzeJava(
        ProjectArchitectureContext ctx,
        List<string> files,
        HashSet<string> dependencies)
    {
        foreach (var file in files.Where(
                     x => x.EndsWith(
                         "pom.xml",
                         StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var xml =
                    File.ReadAllText(file);


                foreach (Match match in Regex.Matches(
                             xml,
                             @"<artifactId>(.*?)</artifactId>"))
                {
                    if (match.Groups.Count <= 1)
                        continue;


                    var dependency =
                        match.Groups[1].Value;


                    dependencies.Add(dependency);


                    ctx.Dependencies.Add(
                        CreateDependency(
                            source: Path.GetFileNameWithoutExtension(file),
                            target: dependency,
                            type: "Maven",
                            file));
                }
            }
            catch
            {
                // Ignore unreadable Maven files
            }
        }
    }



    private static ArchitectureDependencyContext CreateDependency(
        string source,
        string target,
        string type,
        string file)
    {
        return new ArchitectureDependencyContext
        {
            Source = source,

            Target = target,

            DependencyType = type,

            File = file
        };
    }
}