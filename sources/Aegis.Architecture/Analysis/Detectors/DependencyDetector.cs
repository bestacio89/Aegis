using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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

        // Package manifests populate DetectedDependencies only. Internal
        // architecture edges come from explicit project references, never from
        // package names or package versions.
        if (ctx.Modules.Count > 0)
            AnalyzeDotNetProjectReferences(ctx, files);
    }



    private static void AnalyzeDotNetProjectReferences(
        ProjectArchitectureContext context,
        IReadOnlyCollection<string> files)
    {
        context.Dependencies.RemoveAll(dependency =>
            string.Equals(
                dependency.DependencyType,
                "ProjectReference",
                StringComparison.OrdinalIgnoreCase));

        var projectFiles = files
            .Where(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var projectFile in projectFiles)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(projectFile);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or System.Xml.XmlException)
            {
                continue;
            }

            var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(projectFile));
            if (sourceRoot is null)
                continue;

            var sourceModule = context.Modules.FirstOrDefault(module =>
                PathsEqual(module.Path, sourceRoot));
            if (sourceModule is null)
                continue;

            var sourceLayer = sourceModule.Layers.FirstOrDefault()?.Name;
            if (string.IsNullOrWhiteSpace(sourceLayer))
                continue;

            foreach (var reference in document.Descendants()
                         .Where(element => element.Name.LocalName == "ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                    continue;

                var normalizedInclude = include
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var targetProject = Path.GetFullPath(
                    Path.Combine(sourceRoot, normalizedInclude));
                var targetRoot = Path.GetDirectoryName(targetProject);
                if (targetRoot is null)
                    continue;

                var targetModule = context.Modules.FirstOrDefault(module =>
                    PathsEqual(module.Path, targetRoot));
                if (targetModule is null)
                    continue;

                var targetLayer = targetModule.Layers.FirstOrDefault()?.Name;
                if (string.IsNullOrWhiteSpace(targetLayer))
                    continue;

                context.Dependencies.Add(new ArchitectureDependencyContext
                {
                    Source = sourceModule.Name,
                    SourceLayer = sourceLayer,
                    Target = targetModule.Name,
                    TargetLayer = targetLayer,
                    DependencyType = "ProjectReference",
                    File = projectFile
                });
            }
        }

        var unique = context.Dependencies
            .GroupBy(dependency =>
                    $"{dependency.DependencyType}|{dependency.File}|{dependency.Source}|{dependency.Target}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        context.Dependencies = unique.ToList();
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

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


                }
            }
            catch
            {
                // Ignore unreadable Maven files
            }
        }
    }

}