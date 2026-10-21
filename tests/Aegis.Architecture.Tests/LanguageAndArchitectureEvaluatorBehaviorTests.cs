using Aegis.Architecture.Bootstrap;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class LanguageAndArchitectureEvaluatorBehaviorTests
{
    [Fact]
    public async Task NodeEvaluator_reports_dynamic_execution_and_child_process_signals()
    {
        using var project = new TemporaryProject();
        project.Write("back/server.js", """
            const childProcess = require("child_process");

            function executeUserInput(userInput) {
                eval(userInput);
                return childProcess.exec("echo unsafe");
            }
            """);

        var results = await Evaluate(project, "NodeEvaluator", "JavaScript", "Node");

        Assert.Contains(results, result =>
            result.Metrics.GetValueOrDefault("NodeDynamicEvalCount") >= 1);
        Assert.Contains(results, result =>
            result.Metrics.GetValueOrDefault("NodeChildProcessUsageCount") >= 1);
        Assert.All(results, result =>
            Assert.False(string.IsNullOrWhiteSpace(result.Source)));
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task BlazorEvaluator_reports_injection_lifecycle_and_event_binding_signals()
    {
        using var project = new TemporaryProject();
        project.Write("front/Orders.razor", """
            @using System.Collections.Generic
            @using System.Threading.Tasks
            @inject IOrderService OrderService

            <button @onclick="Refresh">Refresh</button>

            @if (Orders.Count > 0)
            {
                <p>Orders loaded</p>
            }

            @code {
                [Inject]
                public IOrderService InjectedService { get; set; } = default!;

                private List<string> Orders = new();

                protected override async Task OnInitializedAsync()
                {
                    await LoadAsync();
                }

                private async void Refresh()
                {
                    await LoadAsync();
                }

                private Task LoadAsync() => Task.CompletedTask;
            }
            """);

        var results = await Evaluate(project, "BlazorEvaluator", "Razor", "Blazor");
        var component = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("BlazorComplianceScore"));

        Assert.True(int.Parse(component.Metadata["InjectionCount"]) >= 1);
        Assert.True(component.Metrics["EventBindingCount"] >= 1);
        Assert.True(component.Metrics["LifecycleMethodCount"] >= 1);
        Assert.Equal("True", component.Metadata["HasLifecycleMethod"]);
        Assert.Equal("True", component.Metadata["HasAsyncVoidMethod"]);
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task BlazorEvaluator_withholds_rule_scores_when_component_code_has_syntax_errors()
    {
        using var project = new TemporaryProject();
        project.Write("front/Orders.razor", """
            <h1>Orders</h1>
            @code {
                private int GetCount()
                {
                    return ;
                }
            }
            """);

        var results = await Evaluate(project, "BlazorEvaluator", "Razor", "Blazor");
        var component = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "File"
                && result.Metadata.GetValueOrDefault("FileName") == "Orders.razor");

        Assert.True(component.Metrics["CSharpSyntaxErrorCount"] > 0);
        Assert.Equal("false", component.Metadata["AnalysisComplete"]);
        Assert.Equal("Incomplete", component.Metadata["AnalysisStatus"]);
        Assert.False(component.Metrics.ContainsKey("ComplexityScore"));
        Assert.False(component.Metrics.ContainsKey("CodeBehindScore"));
        Assert.False(component.Metrics.ContainsKey("DependencyInjectionScore"));

        var summary = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "Project");

        Assert.Equal(1, summary.Metrics["IncompleteComponentCount"]);
        Assert.Equal(-1, summary.Metrics["BlazorHealthIndex"]);
        Assert.Equal("false", summary.Metadata["AnalysisComplete"]);
    }

    [Fact]
    public async Task RazorEvaluator_reports_view_model_injection_and_markup_signals()
    {
        using var project = new TemporaryProject();
        project.Write("front/Orders.cshtml", """
            @model OrdersViewModel
            @inject IOrderService OrderService

            <h1>@Model.Title</h1>
            @if (Model.IsActive)
            {
                <p>Active order</p>
            }
            """);

        var results = await Evaluate(project, "RazorEvaluator", "Razor", "ASP.NET Core MVC");
        var view = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("PresentationSeparationScore")
                && result.Metadata.GetValueOrDefault("EvaluationScope") == "File");

        Assert.True(view.Metrics["MarkupElementCount"] >= 2);
        Assert.True(view.Metrics["InjectDirectiveCount"] >= 1);
        Assert.Equal("True", view.Metadata["HasViewModelReference"]);
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task ArchitecturalEvaluator_does_not_turn_unknown_policy_classification_into_a_violation()
    {
        using var project = new TemporaryProject();
        var context = CreateContext(project, "C#", ".NET");
        context.Dependencies.Add(
            new ArchitectureDependencyContext
            {
                File = "back/OrdersService.cs",
                Source = "OrdersService",
                Target = "ExternalComponent",
                SourceLayer = "UnregisteredLayer",
                TargetLayer = "UnregisteredTargetLayer",
                DependencyType = "Project"
            });

        var results = await Evaluate(project, "ArchitecturalEvaluator", context);
        var dependency = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("Source") == "OrdersService");

        Assert.Equal(0, dependency.Metrics["PolicyClassificationKnown"]);
        Assert.False(dependency.Metrics.ContainsKey("AllowedDependency"));
        Assert.False(dependency.Metrics.ContainsKey("DependencyViolation"));
        Assert.Equal("Unclassified", dependency.Metadata["PolicyDecision"]);

        var summary = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "Project");

        Assert.Equal("Incomplete", summary.Metadata["AnalysisStatus"]);
        Assert.Equal("false", summary.Metadata["AnalysisComplete"]);
        Assert.Equal(1, summary.Metrics["IncompleteDependencyCount"]);
        Assert.False(summary.Metrics.ContainsKey("LayerIsolationCompliance"));
        Assert.False(summary.Metrics.ContainsKey("ForbiddenReferenceCount"));
    }

    private static async Task<ArchitectureEvaluatorResult[]> Evaluate(
        TemporaryProject project,
        string evaluatorName,
        string language,
        string? framework)
    {
        return await Evaluate(project, evaluatorName, CreateContext(project, language, framework));
    }

    private static async Task<ArchitectureEvaluatorResult[]> Evaluate(
        TemporaryProject project,
        string evaluatorName,
        ProjectArchitectureContext context)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAegisCore();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var evaluator = Assert.Single(
            scope.ServiceProvider.GetServices<IEvaluator>(),
            item => item.Name == evaluatorName);

        var results = (await evaluator.EvaluateAsync(
            project.Root,
            context,
            CancellationToken.None)).ToArray();

        AssertAllMetricsAreFinite(results);
        return results;
    }

    private static ProjectArchitectureContext CreateContext(
        TemporaryProject project,
        string language,
        string? framework) =>
        new()
        {
            ProjectName = "LanguageEvaluatorFixture",
            RootPath = project.Root,
            Language = language,
            Framework = framework,
            Confidence = 0.95
        };

    private static void AssertAllMetricsAreFinite(
        IEnumerable<ArchitectureEvaluatorResult> results)
    {
        foreach (var result in results)
        {
            Assert.All(result.Metrics, metric =>
                Assert.True(
                    double.IsFinite(metric.Value),
                    $"Metric '{metric.Key}' was not finite: {metric.Value}."));
        }
    }

    private sealed class TemporaryProject : IDisposable
    {
        public string Root { get; } = Path.Combine(
            FindRepositoryRoot(),
            $"AegisLanguageEvaluator_{Guid.NewGuid():N}");

        public TemporaryProject() => Directory.CreateDirectory(Root);

        public string Write(string relativePath, string content)
        {
            var fullPath = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "sources"))
                    && Directory.Exists(Path.Combine(directory.FullName, "tests")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Aegis repository root for evaluator fixtures.");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
