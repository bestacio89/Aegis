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
        Assert.Equal(100, result.Metrics["BuilderPatternComplianceIndex"]);
        Assert.Equal(0, result.Metrics["BuilderMutabilityViolationCount"]);
        Assert.Equal("FileMetrics", result.Metadata["EvidenceScope.BuilderPatternComplianceIndex"]);
        Assert.Contains("Build()", result.Metadata["Evidence.BuilderPatternComplianceIndex"]);
        Assert.True(result.LineNumber is > 0);
    }

    [Fact]
    public async Task BuilderEvaluator_does_not_require_fluent_chaining_for_a_valid_builder()
    {
        using var project = new TemporaryProject();
        project.Write("back/OrderBuilder.cs", """
            public sealed class OrderBuilder
            {
                private int _id;

                public void WithId(int id) => _id = id;

                public Order Build() => new Order(_id);
            }
            """);

        var results = await Evaluate(project, "BuilderPatternEvaluator");
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("BuilderClassName") == "OrderBuilder");

        Assert.Equal(100, result.Metrics["BuilderPatternComplianceIndex"]);
        Assert.Equal(0, result.Metrics["FluentReturnCount"]);
        Assert.Contains("informational", result.Metadata["BuilderPatternMeasurement"]);
        Assert.Equal("FileMetrics", result.Metadata["EvidenceScope.BuilderPatternComplianceIndex"]);
    }

    [Fact]
    public async Task BuilderEvaluator_does_not_emit_rule_metrics_for_non_candidates()
    {
        using var project = new TemporaryProject();
        project.Write("back/OrderService.cs", """
            public sealed class OrderService
            {
                public Order Get(int id) => new Order(id);
            }

            public sealed class Order
            {
                public Order(int id) => Id = id;
                public int Id { get; }
            }
            """);

        var results = await Evaluate(project, "BuilderPatternEvaluator");

        Assert.DoesNotContain(
            results,
            result => result.Metrics.ContainsKey("BuilderPatternComplianceIndex"));
        Assert.DoesNotContain(
            results,
            result => result.Metrics.ContainsKey("BuilderMutabilityViolationCount"));
    }

    [Fact]
    public async Task FacadeEvaluator_counts_only_public_methods_and_preserves_surface_evidence()
    {
        using var project = new TemporaryProject();
        project.Write("back/OrderFacade.cs", """
            public sealed class OrderFacade
            {
                private readonly IOrderService _orderService;
                private readonly IInvoiceService _invoiceService;

                public Order GetOrder() => _orderService.Get();
                public Invoice GetInvoice() => _invoiceService.Get();

                private void Normalize() { }
            }
            """);
        project.Write("back/InvoiceFacade.ts", """
            export class InvoiceFacade {
                private _service: InvoiceService;

                public getInvoice() {
                    return _service.get();
                }

                private normalize() {
                    return _service.normalize();
                }
            }
            """);

        var results = await Evaluate(project, "FacadePatternEvaluator");
        var order = Assert.Single(
            results,
            item => item.Target == "OrderFacade");
        var invoice = Assert.Single(
            results,
            item => item.Target == "InvoiceFacade");

        Assert.Equal(2, order.Metrics["PublicMethodCount"]);
        Assert.Equal(1, invoice.Metrics["PublicMethodCount"]);
        Assert.Equal("FileMetrics", order.Metadata["EvidenceScope.PublicSurfaceRatio"]);
        Assert.Contains("GetOrder", order.Metadata["Evidence.PublicMethodCount"]);
        Assert.Contains("getInvoice", invoice.Metadata["Evidence.PublicMethodCount"]);
        Assert.DoesNotContain("normalize", invoice.Metadata["Evidence.PublicMethodCount"]);
        Assert.True(order.LineNumber is > 0);
    }

    [Fact]
    public async Task DecoratorEvaluator_emits_abstraction_wrapping_and_delegation_evidence()
    {
        using var project = new TemporaryProject();
        project.Write("back/Decorators/OrderServiceDecorator.cs", """
            public interface IOrderService
            {
                Order Execute();
            }

            public sealed class OrderServiceDecorator : IOrderService
            {
                private readonly IOrderService _inner;

                public OrderServiceDecorator(IOrderService inner) => _inner = inner;

                public Order Execute() => _inner.Execute();
            }
            """);

        var results = await Evaluate(project, "DecoratorPatternEvaluator");
        var result = Assert.Single(
            results,
            item => item.Target == "OrderServiceDecorator");

        Assert.Equal(100, result.Metrics["DecoratorDelegationComplianceScore"]);
        Assert.Equal("FileMetrics", result.Metadata["EvidenceScope.DecoratorDelegationComplianceScore"]);
        Assert.Contains("_inner.Execute", result.Metadata["Evidence.DecoratorDelegationComplianceScore"]);
        Assert.True(result.LineNumber is > 0);
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
        Assert.Equal("false", result.Metadata["SemanticAnalysisPerformed"]);
        Assert.Equal("FileMetrics", result.Metadata["EvidenceScope.FactoryComplianceScore"]);
        Assert.Contains("IOrderFactory", result.Metadata["Evidence.AbstractionScore"]);
        Assert.Contains("FactoryScoreInterpretation", result.Metadata.Keys);
    }

    [Fact]
    public async Task FactoryEvaluator_resolves_abstractions_across_files_and_requires_an_implementation_relationship()
    {
        using var project = new TemporaryProject();
        project.Write("back/Contracts/IOrderFactory.cs", """
            public interface IOrderFactory
            {
                Order Create();
            }
            """);
        project.Write("back/Factories/OrderFactory.cs", """
            public sealed class OrderFactory
            {
                public Order Create() => new Order();
            }
            """);
        project.Write("back/Factories/InvoiceFactory.cs", """
            public sealed class InvoiceFactory : IInvoiceFactory
            {
                public Invoice Create() => new Invoice();
            }
            """);
        project.Write("back/Contracts/IInvoiceFactory.cs", """
            public interface IInvoiceFactory
            {
                Invoice Create();
            }
            """);

        var results = await Evaluate(project, "FactoryPatternEvaluator");
        var order = Assert.Single(results, result =>
            result.Metadata.GetValueOrDefault("FactoryName") == "OrderFactory");
        var invoice = Assert.Single(results, result =>
            result.Metadata.GetValueOrDefault("FactoryName") == "InvoiceFactory");

        Assert.Equal(0, order.Metrics["AbstractionScore"]);
        Assert.Equal(1, invoice.Metrics["AbstractionScore"]);
        Assert.Contains("No matching abstraction", order.Metadata["Evidence.AbstractionScore"]);
        Assert.Contains("IInvoiceFactory.cs", invoice.Metadata["Evidence.AbstractionScore"]);
        Assert.True(invoice.LineNumber is > 0);
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
        Assert.Equal(0, result.Metrics["LeakRisk"]);
        Assert.True(result.Metadata.ContainsKey("HasMatchingAbstraction"));
        Assert.Equal("FileMetrics", result.Metadata["EvidenceScope.InterfaceAdherence"]);
        Assert.Contains("IOrderRepository", result.Metadata["Evidence.InterfaceAdherence"]);
        Assert.Contains("Database access is expected", result.Metadata["ExternalTransportInterpretation"]);
    }

    [Fact]
    public async Task RepositoryEvaluator_resolves_abstractions_across_files_within_the_same_project()
    {
        using var project = new TemporaryProject();
        project.Write("back/Contracts/IOrderRepository.cs", """
            public interface IOrderRepository
            {
                Task<Order> GetAsync(int id);
            }
            """);
        project.Write("back/Persistence/OrderRepository.cs", """
            public sealed class OrderRepository : IOrderRepository
            {
                private readonly HttpClient _httpClient;

                public OrderRepository(HttpClient httpClient) => _httpClient = httpClient;

                public Task<Order> GetAsync(int id) => _httpClient.GetFromJsonAsync<Order>($"/orders/{id}");
            }
            """);

        var results = await Evaluate(project, "RepositoryPatternEvaluator");
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("RepositoryName") == "OrderRepository");

        Assert.Equal(1, result.Metrics["InterfaceAdherence"]);
        Assert.Equal(1, result.Metrics["LeakRisk"]);
        Assert.Contains("IOrderRepository.cs", result.Metadata["Evidence.InterfaceAdherence"]);
        Assert.Contains("HttpClient", result.Metadata["Evidence.LeakRisk"]);
        Assert.True(result.LineNumber is > 0);
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
    public async Task MediatorPatternEvaluator_does_not_treat_handler_dependencies_as_mediator_coupling()
    {
        using var project = new TemporaryProject();
        project.Write("back/Application/OrderHandler.cs", """
            public sealed class OrderHandler : IRequestHandler<CreateOrder, Order>
            {
                private readonly IOrderRepository _orderRepository;

                public void Handle()
                {
                    _orderRepository.Save();
                }
            }
            """);

        var results = await Evaluate(project, "MediatorPatternEvaluator");
        var handler = Assert.Single(
            results,
            result => result.Target.Contains("OrderHandler", StringComparison.OrdinalIgnoreCase));

        Assert.False(handler.Metrics.ContainsKey("CouplingRisk"));
        Assert.False(handler.Metrics.ContainsKey("DirectServiceLikeCallCount"));
    }

    [Fact]
    public async Task MediatorPatternEvaluator_emits_coupling_evidence_for_mediator_implementations()
    {
        using var project = new TemporaryProject();
        project.Write("back/Infrastructure/OrderMediator.cs", """
            public sealed class OrderMediator : IMediator
            {
                private readonly IOrderService _orderService;

                public void Dispatch()
                {
                    _orderService.Execute();
                }
            }
            """);

        var results = await Evaluate(project, "MediatorPatternEvaluator");
        var mediator = Assert.Single(
            results,
            result => result.Target == "OrderMediator");

        Assert.Equal(1, mediator.Metrics["CouplingRisk"]);
        Assert.Equal("FileMetrics", mediator.Metadata["EvidenceScope.CouplingRisk"]);
        Assert.Contains("_orderService.Execute", mediator.Metadata["Evidence.CouplingRisk"]);
        Assert.Contains("_orderService.Execute", mediator.Metadata["CodeSnippet.CouplingRisk"]);
        Assert.True(mediator.LineNumber is > 0);
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
        Assert.Equal(1, result.Metrics["AnemicDomainModelIndex"]);
        Assert.Equal(0, result.Metrics["Methods"]);
        Assert.Equal(0, result.Metrics["BehaviorRatio"]);
        Assert.Equal(bool.FalseString, result.Metadata["SemanticAnalysisPerformed"]);
        Assert.Equal("FileMetrics", result.Metadata["EvidenceScope.AnemicDomainModelIndex"]);
        Assert.Contains("OrderEntity", result.Evidence);
        Assert.Contains("properties", result.Metadata["Evidence.AnemicDomainModelIndex"]);
    }



    [Fact]
    public async Task GodClassEvaluator_withholds_density_when_no_classes_were_analyzed()
    {
        using var project = new TemporaryProject();
        project.Write("back/Empty.cs", "namespace Empty;");

        var results = await Evaluate(project, "GodClassEvaluator");
        var summary = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "Project");

        Assert.False(summary.Metrics.ContainsKey("GodClassDensity"));
    }

    [Fact]
    public async Task AnemicDomainModelEvaluator_excludes_dtos_services_records_and_abstract_base_types()
    {
        using var project = new TemporaryProject();
        project.Write("back/Domain/DomainSupportTypes.cs", """
            public sealed class OrderDto
            {
                public int Id { get; set; }
            }

            public sealed class DomainService
            {
                public string Name { get; set; } = string.Empty;
            }

            public abstract class Entity<T>
            {
                public T Id { get; set; }
            }

            public record Currency
            {
                public decimal Amount { get; init; }
            }
            """);

        var results = await Evaluate(project, "AnemicDomainModelEvaluator");

        Assert.DoesNotContain(
            results,
            result => result.Metrics.ContainsKey("AnemicDomainModelIndex"));
    }

    [Fact]
    public async Task AnemicDomainModelEvaluator_does_not_treat_stateless_types_as_candidates()
    {
        using var project = new TemporaryProject();
        project.Write("back/Domain/OrderEntity.cs", """
            public sealed class OrderEntity
            {
                public void Validate() { }
            }
            """);

        var results = await Evaluate(project, "AnemicDomainModelEvaluator");

        Assert.DoesNotContain(
            results,
            result => result.Metrics.ContainsKey("AnemicDomainModelIndex"));
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
        Assert.Equal(bool.FalseString, result.Metadata["RuntimeUniquenessVerified"]);
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

            public sealed class CreateOrderHandler
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
        Assert.Equal("CreateOrderHandler", result.Metadata["ExpectedHandler"]);
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
            Enumerable.Range(0, 21).Select(index =>
            {
                var statements = string.Join(
                    Environment.NewLine,
                    Enumerable.Range(0, 30).Select(line =>
                        $"        var value{index}_{line} = {line};"));

                return $@"    public void Operation{index}()
    {{
{statements}
    }}";
            }));

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

        Assert.True(result.Metrics["MethodCount"] >= 21);
        Assert.True(result.Metrics["PropertyCount"] >= 2);
        Assert.True(result.Metrics["GodClassSeverity"] > 1.0);
        Assert.True(result.Metrics.ContainsKey("GodClassComplianceScore"));
        Assert.Contains(results, item =>
            item.Metadata.GetValueOrDefault("EvaluationScope") == "Project"
            && item.Metrics.ContainsKey("GodClassDensity"));
    }

    [Fact]
    public async Task GodClassEvaluator_does_not_flag_data_heavy_type_without_method_bloat()
    {
        using var project = new TemporaryProject();
        var entries = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 650).Select(index =>
                $"        new SeedEntry({index}),"));

        project.Write(
            "back/HeroSeeder.cs",
            $@"public sealed class HeroSeeder
{{
    public IEnumerable<SeedEntry> GetSeeds()
    {{
        return new SeedEntry[]
        {{
{entries}
        }};
    }}
}}
public sealed record SeedEntry(int Id);");

        var results = await Evaluate(project, "GodClassEvaluator");
        var result = Assert.Single(
            results,
            item => item.Metadata.GetValueOrDefault("ClassName") == "HeroSeeder");

        Assert.True(result.Metrics["LineLimitRatio"] > 1);
        Assert.True(result.Metrics["MethodLimitRatio"] < 1);
        Assert.True(result.Metrics["GodClassSeverity"] < 1);
    }

    [Fact]
    public async Task GodClassEvaluator_excludes_vendored_unity_examples_and_generated_files()
    {
        using var project = new TemporaryProject();
        Directory.CreateDirectory(Path.Combine(project.Root, "Assets"));
        Directory.CreateDirectory(Path.Combine(project.Root, "ProjectSettings"));

        var largeClass = $@"public sealed class ExampleService
{{
{string.Join(Environment.NewLine, Enumerable.Range(0, 30).Select(index => $"    public void Operation{index}() {{ }}"))}
}}";

        project.Write("Assets/Scripts/FirstPartyService.cs", largeClass);
        project.Write("Assets/TextMesh Pro/Examples & Extras/Scripts/VendoredExample.cs", largeClass);
        project.Write("Assets/Scripts/GeneratedService.Designer.cs", largeClass);

        var results = await Evaluate(project, "GodClassEvaluator");

        Assert.Contains(results, item =>
            item.Metadata.GetValueOrDefault("ClassName") == "ExampleService"
            && item.Metadata.GetValueOrDefault("FilePath")?.Contains("FirstPartyService.cs", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(results, item =>
            item.Metadata.GetValueOrDefault("FilePath")?.Contains("Examples & Extras", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(results, item =>
            item.Metadata.GetValueOrDefault("FilePath")?.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) == true);
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
