using Aegis.Architecture.Bootstrap;
using Aegis.Architecture.Evaluators.DesignPatterns;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class DesignPatternEvaluatorBehaviorTests
{
    [Fact]
    public async Task BuilderEvaluator_reports_build_and_fluent_mutation_signals()
    {
        using var project = new TemporaryProject();
        project.Write("back/FluentOrderAssembler.cs", """
            public sealed class OrderBuilder
            {
                private string _id = string.Empty;

                public OrderBuilder WithId(string id)
                {
                    _id = id;
                    return this;
                }

                public Order Build() => new Order(_id);
            }
            """);

        var results = await Evaluate(project, "BuilderPatternEvaluator");
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("BuilderClassName") == "OrderBuilder");

        Assert.Equal(1, result.Metrics["HasBuildMethod"]);
        Assert.True(result.Metrics["BuildMethodCount"] >= 1);
        Assert.True(result.Metrics["FluentReturnCount"] >= 1);
        Assert.True(result.Metrics["MutationCount"] >= 1);
        Assert.Equal("File", result.Metadata["EvaluationScope"]);
    }

    [Fact]
    public async Task FactoryEvaluator_reports_instantiation_and_abstraction_evidence()
    {
        using var project = new TemporaryProject();
        project.Write("back/OrderFactory.cs", """
            public interface IOrderFactory
            {
                Order Create();
            }

            public sealed class OrderFactory : IOrderFactory
            {
                public Order Create()
                {
                    return new Order();
                }
            }

            public sealed class Order { }
            """);

        var results = await Evaluate(project, "FactoryPatternEvaluator");
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("FactoryName") == "OrderFactory");

        Assert.True(result.Metrics["InstantiationCount"] >= 1);
        Assert.Equal(1, result.Metrics["AbstractionScore"]);
        Assert.True(result.Metrics.ContainsKey("FactoryComplianceScore"));
        Assert.Equal(bool.FalseString, result.Metadata["SemanticAnalysisPerformed"]);
    }

    [Fact]
    public async Task RepositoryEvaluator_measures_repository_abstraction_and_async_signals()
    {
        using var project = new TemporaryProject();
        project.Write("back/OrderRepository.cs", """
            using System.Threading.Tasks;

            public interface IOrderRepository
            {
                Task<Order> GetAsync(int id);
            }

            public sealed class OrderRepository : IOrderRepository
            {
                private readonly DbContext _context;

                public OrderRepository(DbContext context) => _context = context;

                public async Task<Order> GetAsync(int id)
                {
                    return await _context.FindAsync<Order>(id);
                }
            }

            public sealed class DbContext
            {
                public Task<T> FindAsync<T>(int id) => Task.FromResult(default(T)!);
            }

            public sealed class Order { }
            """);

        var results = await Evaluate(project, "RepositoryPatternEvaluator");
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("RepositoryName") == "OrderRepository");

        Assert.True(result.Metrics.ContainsKey("RepositoryComplianceScore"));
        Assert.True(result.Metrics["MethodCount"] >= 1);
        Assert.True(result.Metrics.ContainsKey("AsyncCompliance"));
        Assert.True(result.Metadata.ContainsKey("HasMatchingAbstraction"));
    }

    [Fact]
    public async Task StrategyEvaluator_detects_strategy_abstraction_and_implementation()
    {
        using var project = new TemporaryProject();
        project.Write("back/DiscountStrategy.cs", """
            public interface IDiscountStrategy
            {
                decimal Apply(decimal amount);
            }

            public sealed class HolidayStrategy : IDiscountStrategy
            {
                public decimal Apply(decimal amount)
                {
                    return amount * 0.9m;
                }
            }
            """);

        var policy = new AegisArchitecturePolicy();
        policy.Architecture.DesignPatterns.EnforceStrategyPattern = true;
        var evaluator = new StrategyPatternEvaluator(
            NullLogger<StrategyPatternEvaluator>.Instance,
            Options.Create(policy));

        var results = await Evaluate(project, evaluator);
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("ClassName") == "HolidayStrategy");

        Assert.Equal(1, result.Metrics["HasStrategy"]);
        Assert.True(result.Metrics.ContainsKey("StrategyComplianceScore"));
        Assert.True(result.Metrics.ContainsKey("InterfaceAdherence"));
    }

    [Fact]
    public async Task AnemicDomainModelEvaluator_reports_state_to_behavior_signals()
    {
        using var project = new TemporaryProject();
        project.Write("back/Domain/OrderEntity.cs", """
            public sealed class OrderEntity
            {
                public int Id { get; set; }
                public string CustomerName { get; set; } = string.Empty;
            }
            """);

        var results = await Evaluate(project, "AnemicDomainModelEvaluator");
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("ClassName") == "OrderEntity");

        Assert.Equal(2, result.Metrics["Properties"]);
        Assert.Equal(2, result.Metrics["StateMemberCount"]);
        Assert.Equal(0, result.Metrics["Methods"]);
        Assert.Equal(0, result.Metrics["BehaviorRatio"]);
        Assert.Equal("false", result.Metadata["SemanticAnalysisPerformed"]);
    }

    [Fact]
    public async Task SingletonEvaluator_reports_static_instance_and_private_constructor_signals()
    {
        using var project = new TemporaryProject();
        project.Write("back/ApplicationSingleton.cs", """
            public sealed class ApplicationSingleton
            {
                private static readonly ApplicationSingleton _instance =
                    new ApplicationSingleton();

                private ApplicationSingleton() { }

                public static ApplicationSingleton Instance => _instance;
            }
            """);

        var results = await Evaluate(project, "SingletonPatternEvaluator");
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("ClassName") == "ApplicationSingleton");

        Assert.Equal(1, result.Metrics["IsSingleton"]);
        Assert.Equal(1, result.Metrics["HasStaticInstanceMember"]);
        Assert.Equal(1, result.Metrics["HasPrivateConstructor"]);
        Assert.True(result.Metrics.ContainsKey("SingletonComplianceScore"));
        Assert.Equal("false", result.Metadata["RuntimeUniquenessVerified"]);
    }

    [Fact]
    public async Task CommandEvaluator_matches_command_types_to_handler_types()
    {
        using var project = new TemporaryProject();
        project.Write("back/CreateOrderCommand.cs", """
            public sealed class CreateOrderCommand
            {
                public int OrderId { get; init; }
            }

            public sealed class CreateOrderCommandHandler
            {
                public void Handle(CreateOrderCommand command)
                {
                    _ = command.OrderId;
                }
            }
            """);

        var policy = new AegisArchitecturePolicy();
        policy.Architecture.DesignPatterns.EnforceCommandPattern = true;
        var evaluator = new CommandPatternEvaluator(
            NullLogger<CommandPatternEvaluator>.Instance,
            Options.Create(policy));

        var results = await Evaluate(project, evaluator);
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("CommandName") == "CreateOrderCommand");

        Assert.Equal(1, result.Metrics["HasHandlerPair"]);
        Assert.Equal("CreateOrderCommandHandler", result.Metadata["ExpectedHandler"]);
        Assert.True(result.Metrics.ContainsKey("CommandComplianceScore"));
    }

    [Fact]
    public async Task FacadeEvaluator_reports_dependency_surface_and_delegation()
    {
        using var project = new TemporaryProject();
        project.Write("back/OrderFacade.cs", """
            public sealed class OrderFacade
            {
                private readonly OrderService OrderService;
                private readonly PaymentService PaymentService;

                public OrderFacade(OrderService OrderService, PaymentService PaymentService)
                {
                    this.OrderService = OrderService;
                    this.PaymentService = PaymentService;
                }

                public void Checkout(Order order)
                {
                    OrderService.Save(order);
                    PaymentService.Charge(order);
                }
            }

            public sealed class OrderService
            {
                public void Save(Order order) { }
            }

            public sealed class PaymentService
            {
                public void Charge(Order order) { }
            }

            public sealed class Order { }
            """);

        var results = await Evaluate(project, "FacadePatternEvaluator");
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("ClassName") == "OrderFacade");

        Assert.True(result.Metrics["HasConstructorInjection"] >= 1);
        Assert.True(result.Metrics["HasDelegationCalls"] >= 1);
        Assert.True(result.Metrics.ContainsKey("DependencyRatio"));
        Assert.Equal("true", result.Metadata["StructuralAnalysisHeuristic"]);
    }

    [Fact]
    public async Task GodClassEvaluator_counts_methods_and_properties_in_large_types()
    {
        using var project = new TemporaryProject();
        var methods = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 14).Select(index =>
                $"    public void Operation{index}() {{ }}"));

        project.Write(
            "back/OrderManager.cs",
            $@"public sealed class OrderManager
{{
    public int Id {{ get; set; }}
    public string Name {{ get; set; }} = string.Empty;
{methods}
}}");

        var results = await Evaluate(project, "GodClassEvaluator");
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("ClassName") == "OrderManager");

        Assert.True(result.Metrics["MethodCount"] >= 14);
        Assert.True(result.Metrics["PropertyCount"] >= 2);
        Assert.True(result.Metrics.ContainsKey("GodClassComplianceScore"));
    }

    private static Task<ArchitectureEvaluatorResult[]> Evaluate(
        TemporaryProject project,
        string evaluatorName)
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

        return Evaluate(project, evaluator);
    }

    private static async Task<ArchitectureEvaluatorResult[]> Evaluate(
        TemporaryProject project,
        IEvaluator evaluator)
    {
        var context = new ProjectArchitectureContext
        {
            ProjectName = "DesignPatternFixture",
            RootPath = project.Root,
            Language = "C#",
            Framework = "ASP.NET Core",
            Confidence = 0.95
        };

        var results = (await evaluator.EvaluateAsync(
            project.Root,
            context,
            CancellationToken.None)).ToArray();

        AssertAllMetricsAreFinite(results);
        return results;
    }

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
            $"AegisDesignPatternBehavior_{Guid.NewGuid():N}");

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
                "Could not locate the Aegis repository root for design-pattern fixtures.");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
