using System.Xml.Linq;
using Xunit;

namespace Aegis.Quality.Tests;

public sealed class ProjectDependencyTests
{
    [Fact]
    public void Non_ui_source_projects_do_not_reference_wpf()
    {
        var root = FindRepositoryRoot();
        var sourceProjects = Directory
            .EnumerateFiles(Path.Combine(root, "sources"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsWpfProject(path))
            .ToArray();

        Assert.NotEmpty(sourceProjects);

        var violations = sourceProjects
            .SelectMany(projectPath => GetProjectReferences(projectPath)
                .Where(reference => IsWpfReference(projectPath, reference))
                .Select(reference => $"{Path.GetRelativePath(root, projectPath)} -> {reference}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Non-WPF production projects must not depend on the WPF presentation layer:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Source_project_reference_graph_is_acyclic()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "sources");
        var projects = Directory
            .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetFullPath(path),
                path => GetProjectReferences(path)
                    .Select(reference => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, reference)))
                    .Where(File.Exists)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycle = FindCycle(projects, completed, active, new List<string>());

        Assert.True(
            cycle is null,
            cycle is null
                ? string.Empty
                : "Project references must form a directed acyclic graph. Cycle: "
                    + string.Join(" -> ", cycle.Select(path => Path.GetRelativePath(root, path))));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aegis.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate Aegis.slnx above the test assembly output directory.");
    }

    private static IEnumerable<string> GetProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!.Replace('\\', Path.DirectorySeparatorChar));
    }

    private static bool IsWpfProject(string projectPath) =>
        Path.GetFileName(projectPath).Equals("Aegis.Wpf.csproj", StringComparison.OrdinalIgnoreCase)
        || projectPath.Contains($"{Path.DirectorySeparatorChar}Aegis.App.WPF{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static bool IsWpfReference(string projectPath, string reference)
    {
        var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, reference));
        return Path.GetFileName(target).Equals("Aegis.Wpf.csproj", StringComparison.OrdinalIgnoreCase)
            || target.Contains($"{Path.DirectorySeparatorChar}Aegis.App.WPF{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string>? FindCycle(
        IReadOnlyDictionary<string, string[]> graph,
        ISet<string> completed,
        ISet<string> active,
        List<string> path)
    {
        foreach (var node in graph.Keys)
        {
            var cycle = Visit(node, graph, completed, active, path);
            if (cycle is not null)
            {
                return cycle;
            }
        }
        return null;
    }

    private static IReadOnlyList<string>? Visit(
        string node,
        IReadOnlyDictionary<string, string[]> graph,
        ISet<string> completed,
        ISet<string> active,
        List<string> path)
    {
        if (completed.Contains(node))
        {
            return null;
        }
        if (active.Contains(node))
        {
            var start = path.FindIndex(item => item.Equals(node, StringComparison.OrdinalIgnoreCase));
            return path.Skip(Math.Max(start, 0)).Append(node).ToArray();
        }

        active.Add(node);
        path.Add(node);
        if (graph.TryGetValue(node, out var dependencies))
        {
            foreach (var dependency in dependencies.Where(graph.ContainsKey))
            {
                var cycle = Visit(dependency, graph, completed, active, path);
                if (cycle is not null)
                {
                    return cycle;
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        active.Remove(node);
        completed.Add(node);
        return null;
    }
}
