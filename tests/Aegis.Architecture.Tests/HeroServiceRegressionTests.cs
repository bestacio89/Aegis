using Aegis.Architecture.Analysis;
using Aegis.Architecture.Bootstrap;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class HeroServiceRegressionTests
{
    [Fact]
    public async Task Pinned_HeroService_snapshot_preserves_backend_blazor_and_unity_evidence()
    {
        using var project = new TemporaryProject();
        CopyPinnedSnapshot(project.Root);

        var rootContext = ProjectArchitectureContextDetector.Detect(project.Root);

        Assert.Equal("ASP.NET Core", rootContext.Framework);
        Assert.Contains("ASP.NET Core", rootContext.DetectedFrameworks);
        Assert.Contains("Blazor", rootContext.DetectedFrameworks);
        Assert.Contains("Unity", rootContext.DetectedFrameworks);
        Assert.Equal("FullStack", rootContext.DomainType);

        var apiPath = Path.Combine(project.Root, "sources", "back", "HeroService.API");
        var blazorPath = Path.Combine(project.Root, "sources", "Front", "HeroService.Admin");
        var unityPath = Path.Combine(project.Root, "sources", "client");

        Assert.Contains(rootContext.Modules, module => module.Name == "HeroService.API");
        Assert.Contains(rootContext.Modules, module => module.Name == "HeroService.Admin");
        Assert.Contains(rootContext.Modules, module => module.Type == "Unity"
            && string.Equals(module.Path, unityPath, StringComparison.OrdinalIgnoreCase));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAegisCore();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var evaluators = scope.ServiceProvider.GetServices<IEvaluator>().ToArray();
        var namingEvaluator = Assert.Single(evaluators, evaluator => evaluator.Name == "NamingEvaluator");
        var blazorEvaluator = Assert.Single(evaluators, evaluator => evaluator.Name == "BlazorEvaluator");

        var apiContext = ProjectArchitectureContextDetector.Detect(apiPath);
        PreserveRepositoryGraph(apiContext, rootContext);
        var apiFacts = (await namingEvaluator.EvaluateAsync(apiPath, apiContext)).ToArray();

        Assert.Contains(apiFacts, fact =>
            fact.Metadata.TryGetValue("FilePath", out var path)
            && path.EndsWith("HeroController.cs", StringComparison.OrdinalIgnoreCase)
            && fact.Metadata.GetValueOrDefault("Framework") == "ASP.NET Core");

        var blazorContext = ProjectArchitectureContextDetector.Detect(blazorPath);
        PreserveRepositoryGraph(blazorContext, rootContext);
        var blazorFacts = (await blazorEvaluator.EvaluateAsync(blazorPath, blazorContext)).ToArray();

        Assert.Contains(blazorFacts, fact =>
            fact.Metadata.TryGetValue("FilePath", out var path)
            && path.EndsWith("Home.razor", StringComparison.OrdinalIgnoreCase)
            && fact.Metadata.GetValueOrDefault("Framework") == "Blazor");

        var unityContext = ProjectArchitectureContextDetector.Detect(unityPath);
        PreserveRepositoryGraph(unityContext, rootContext);
        var unityFacts = (await namingEvaluator.EvaluateAsync(unityPath, unityContext)).ToArray();

        Assert.Contains(unityFacts, fact =>
            fact.Metadata.TryGetValue("FilePath", out var path)
            && path.EndsWith("HeroCatalogService.cs", StringComparison.OrdinalIgnoreCase)
            && fact.Metadata.GetValueOrDefault("Framework") == "Unity");
    }

    private static void PreserveRepositoryGraph(
        Aegis.Shared.Architecture.Models.ProjectArchitectureContext moduleContext,
        Aegis.Shared.Architecture.Models.ProjectArchitectureContext rootContext)
    {
        moduleContext.Modules = rootContext.Modules;
        moduleContext.Layers = rootContext.Layers;
        moduleContext.Dependencies = rootContext.Dependencies;
        moduleContext.Boundaries = rootContext.Boundaries;
        moduleContext.PatternsDetected = rootContext.PatternsDetected;
    }

    private static void CopyPinnedSnapshot(string destinationRoot)
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            repositoryRoot,
            "tests",
            "Aegis.Architecture.Tests",
            "Fixtures",
            "HeroService");

        foreach (var fixture in Directory.EnumerateFiles(
                     fixtureRoot,
                     "*.fixture",
                     SearchOption.AllDirectories))
        {
            var relativeFixturePath = Path.GetRelativePath(fixtureRoot, fixture);
            var relativeTargetPath = relativeFixturePath[..^".fixture".Length];
            var target = Path.Combine(destinationRoot, relativeTargetPath);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(fixture, target, overwrite: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "tests",
                    "Aegis.Architecture.Tests",
                    "Aegis.Architecture.Tests.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Aegis repository root for HeroService regression fixtures.");
    }

    private sealed class TemporaryProject : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            $"AegisHeroServiceRegression_{Guid.NewGuid():N}");

        public TemporaryProject() => Directory.CreateDirectory(Root);

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
