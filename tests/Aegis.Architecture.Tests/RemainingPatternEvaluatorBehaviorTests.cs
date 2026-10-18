using Aegis.Architecture.Evaluators.DesignPatterns;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class RemainingPatternEvaluatorBehaviorTests
{
    [Fact]
    public async Task ObserverPatternEvaluator_measures_publication_subscription_and_lifecycle_signals()
    {
        using var project = new TemporaryProject();
        project.Write("back/OrderObserver.cs", """
            public interface IEventPublisher
            {
                void Publish(OrderCreated message);
                void Subscribe(System.Action<OrderCreated> handler);
                void Unsubscribe(System.Action<OrderCreated> handler);
            }

            public sealed class OrderObserver : System.IDisposable
            {
                private readonly IEventPublisher _publisher;

                public OrderObserver(IEventPublisher publisher)
                {
                    _publisher = publisher;
                }

                public void Start()
                {
                    _publisher.Subscribe(Handle);
                }

                public void Notify()
                {
                    _publisher.Publish(new OrderCreated());
                }

                public void Dispose()
                {
                    _publisher.Unsubscribe(Handle);
                }

                private void Handle(OrderCreated message) { }
            }

            public sealed class OrderCreated { }
            """);

        var policy = new AegisArchitecturePolicy();
        policy.Architecture.DesignPatterns.EnforceObserverPattern = true;
        var evaluator = new ObserverPatternEvaluator(
            NullLogger<ObserverPatternEvaluator>.Instance,
            Options.Create(policy));

        var results = (await evaluator.EvaluateAsync(
            project.Root,
            CreateContext(project, "C#", "ASP.NET"))).ToArray();

        var result = Assert.Single(
            results,
            item => item.Metrics.ContainsKey("ObserverComplianceScore")
                && item.Metadata.GetValueOrDefault("EvaluationScope") == "File");

        Assert.Equal(1, result.Metrics["IsObserver"]);
        Assert.True(result.Metrics["PublishCount"] >= 1);
        Assert.True(result.Metrics["SubscriptionCount"] >= 1);
        Assert.Equal(1, result.Metrics["LifecycleManaged"]);
        Assert.Equal(0, result.Metrics["LeakRisk"]);
        Assert.Equal("false", result.Metadata["SemanticAnalysisPerformed"]);
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task MediatorPatternEvaluator_measures_handler_contracts_and_mediator_calls()
    {
        using var project = new TemporaryProject();
        project.Write("back/CreateOrderHandler.cs", """
            public interface IRequestHandler { }

            public sealed class CreateOrderHandler : IRequestHandler
            {
                public void Handle(CreateOrderCommand command) { }
            }

            public sealed class CreateOrderCommand { }

            public interface IMediator
            {
                void Send(object request);
            }

            public sealed class OrderApplicationService
            {
                private readonly IMediator _mediator;

                public OrderApplicationService(IMediator mediator)
                {
                    _mediator = mediator;
                }

                public void Execute(CreateOrderCommand command)
                {
                    _mediator.Send(command);
                }
            }
            """);

        var policy = new AegisArchitecturePolicy();
        policy.Architecture.DesignPatterns.EnforceMediatorPattern = true;
        policy.Architecture.DesignPatterns.MediatorApplicableLanguages["C#"] = true;

        var evaluator = new MediatorPatternEvaluator(
            NullLogger<MediatorPatternEvaluator>.Instance,
            Options.Create(policy));

        var results = (await evaluator.EvaluateAsync(
            project.Root,
            CreateContext(project, "C#", "ASP.NET"))).ToArray();

        var result = Assert.Single(
            results,
            item => item.Metrics.ContainsKey("MediatorComplianceScore")
                && item.Metadata.GetValueOrDefault("EvaluationScope") == "File");

        Assert.True(result.Metrics["HandlerCount"] >= 1);
        Assert.True(result.Metrics["MediatorCallCount"] >= 1);
        Assert.True(result.Metrics["MediatorHandlerContractCount"] >= 1);
        Assert.Equal(1, result.Metrics["UsesMediator"]);
        Assert.Equal(bool.FalseString, result.Metadata["SemanticAnalysisPerformed"]);
        AssertAllMetricsAreFinite(results);
    }

    private static ProjectArchitectureContext CreateContext(
        TemporaryProject project,
        string language,
        string? framework) =>
        new()
        {
            ProjectName = "PatternEvaluatorFixture",
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
            $"AegisPatternEvaluator_{Guid.NewGuid():N}");

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
                "Could not locate the Aegis repository root for pattern fixtures.");
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
