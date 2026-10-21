using Aegis.Architecture.Bootstrap;
using Aegis.Architecture.Evaluators.Persistence;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureEvaluatorBehaviorTests
{
    [Fact]
    public async Task DocumentationEvaluator_distinguishes_documented_from_undocumented_declarations()
    {
        using var project = new TemporaryProject();
        project.Write("back/DocumentationFixture.cs", """
            /// <summary>Documented service.</summary>
            public class DocumentationFixture
            {
                /// <summary>Documented operation.</summary>
                public void DocumentedOperation() { }

                public void UndocumentedOperation() { }
            }
            """);

        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var evaluator = GetEvaluator(scope.ServiceProvider, "DocumentationEvaluator");

        var results = (await evaluator.EvaluateAsync(project.Root, CreateContext(project))).ToArray();
        var summary = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "Project");

        Assert.Equal(3, summary.Metrics["TotalDeclarations"]);
        Assert.Equal(2, summary.Metrics["DocumentedDeclarations"]);
        Assert.Equal(1, summary.Metrics["MissingDocumentation"]);
        Assert.Equal(200d / 3d, summary.Metrics["DocumentationCoverage"], precision: 5);
        Assert.Equal("Completed", summary.Metadata["AnalysisStatus"]);
    }

    [Fact]
    public async Task ComplexityEvaluator_reports_a_method_exceeding_the_complexity_threshold()
    {
        using var project = new TemporaryProject();

        const int decisionCount = 16;
        var parameters = string.Join(
            ", ",
            Enumerable.Range(0, decisionCount).Select(index => $"bool condition{index}"));
        var decisions = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, decisionCount)
                .Select(index => $"        if (condition{index}) {{ count++; }}"));

        var source = $@"public sealed class ComplexityFixture
{{
    public int HighComplexity({parameters})
    {{
        var count = 0;
{decisions}
        return count;
    }}
}}";

        project.Write("back/ComplexityFixture.cs", source);

        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var evaluator = GetEvaluator(scope.ServiceProvider, "ComplexityEvaluator");

        var results = (await evaluator.EvaluateAsync(project.Root, CreateContext(project))).ToArray();
        var finding = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("MethodName") == "HighComplexity");

        Assert.True(finding.Metrics["CyclomaticComplexity"] > 10);
        Assert.Equal(1, finding.Metrics["ComplexityFinding"]);
        Assert.Equal("SourcePatternHeuristic", finding.Metadata["MeasurementMethod"]);
    }

    [Fact]
    public async Task ComplexityEvaluator_emits_compliant_methods_for_complete_coverage()
    {
        using var project = new TemporaryProject();
        project.Write("back/SimpleFixture.cs", """
            public sealed class SimpleFixture
            {
                public void Execute()
                {
                    var value = 1;
                    _ = value;
                }
            }
            """);

        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var evaluator = GetEvaluator(scope.ServiceProvider, "ComplexityEvaluator");

        var results = (await evaluator.EvaluateAsync(
            project.Root,
            CreateContext(project))).ToArray();

        var measurement = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("MethodName") == "Execute");

        Assert.Equal(0, measurement.Metrics["ComplexityFinding"]);
        Assert.True(measurement.Metrics.ContainsKey("EstimatedCyclomaticComplexity"));
        Assert.Equal("true", measurement.Metadata["AnalysisComplete"]);
        Assert.Equal("Completed", measurement.Metadata["AnalysisStatus"]);
    }

    [Fact]
    public async Task ErrorHandlingEvaluator_reports_empty_catches_and_a_project_summary()
    {
        using var project = new TemporaryProject();
        project.Write("back/ErrorHandlingFixture.cs", """
            public sealed class ErrorHandlingFixture
            {
                public void Execute()
                {
                    try
                    {
                        throw new System.InvalidOperationException();
                    }
                    catch (System.Exception)
                    {
                    }
                }
            }
            """);

        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var evaluator = GetEvaluator(scope.ServiceProvider, "ErrorHandlingEvaluator");

        var results = (await evaluator.EvaluateAsync(project.Root, CreateContext(project))).ToArray();
        var fileResult = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "File");
        var summary = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("EvaluationScope") == "Project");

        Assert.Equal(1, fileResult.Metrics["TotalCatchBlocks"]);
        Assert.Equal(1, fileResult.Metrics["EmptyCatchBlocks"]);
        Assert.Equal(1, fileResult.Metrics["SwallowedExceptions"]);
        Assert.Equal(1, summary.Metrics["FilesAnalyzed"]);
        Assert.Equal(1, summary.Metrics["TotalCatchBlocks"]);
    }

    [Fact]
    public async Task SecurityEvaluator_detects_a_hardcoded_password_and_preserves_evidence()
    {
        using var project = new TemporaryProject();
        project.Write("back/SecurityFixture.cs", """
            public sealed class SecurityFixture
            {
                private const string password = "SuperSecret123";
            }
            """);

        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var evaluator = GetEvaluator(scope.ServiceProvider, "SecurityEvaluator");

        var results = (await evaluator.EvaluateAsync(project.Root, CreateContext(project))).ToArray();
        var finding = Assert.Single(
            results,
            result => result.Target == "SecurityFixture.cs"
                && result.Metrics.ContainsKey("SecretsDetected"));

        Assert.True(finding.Metrics["SecretsDetected"] >= 1);
        Assert.False(string.IsNullOrWhiteSpace(finding.Target));
        Assert.Contains(results, result =>
            result.Metadata.GetValueOrDefault("EvaluationScope") == "Project");
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task LoggingEvaluator_measures_logging_calls_and_emits_summary()
    {
        using var project = new TemporaryProject();
        project.Write("back/LoggingFixture.cs", """
            public sealed class LoggingFixture
            {
                public void Execute(Microsoft.Extensions.Logging.ILogger logger, string orderId)
                {
                    logger.LogInformation("Processing order {OrderId}", orderId);
                    System.Console.WriteLine($"Order {orderId}");
                }
            }
            """);
        project.Write(
            "appsettings.json",
            """{"Logging":{"LogLevel":{"Default":"Information"}}}""");

        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var evaluator = GetEvaluator(scope.ServiceProvider, "LoggingEvaluator");

        var results = (await evaluator.EvaluateAsync(project.Root, CreateContext(project))).ToArray();
        var fileResult = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("LoggingCallCount"));

        Assert.True(fileResult.Metrics["LoggingCallCount"] >= 2);
        Assert.True(fileResult.Metrics.ContainsKey("StructuredLoggingScore"));
        Assert.Contains(results, result =>
            result.Metrics.ContainsKey("ObservabilityHealthIndex"));
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task PerformanceEvaluator_detects_nested_loops_blocking_calls_and_synchronous_io()
    {
        using var project = new TemporaryProject();
        project.Write("back/PerformanceFixture.cs", """
            public sealed class PerformanceFixture
            {
                public string Execute()
                {
                    for (var outer = 0; outer < 10; outer++)
                    {
                        for (var inner = 0; inner < 10; inner++)
                        {
                            System.Threading.Thread.Sleep(1);
                            System.IO.File.ReadAllText("fixture.txt");
                        }
                    }

                    return string.Empty;
                }
            }
            """);

        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var evaluator = GetEvaluator(scope.ServiceProvider, "PerformanceEvaluator");

        var results = (await evaluator.EvaluateAsync(project.Root, CreateContext(project))).ToArray();
        var fileResult = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("PerformanceHealthIndex")
                && result.Metadata.GetValueOrDefault("FilePath")?.EndsWith(
                    "PerformanceFixture.cs",
                    StringComparison.OrdinalIgnoreCase) == true);

        Assert.True(fileResult.Metrics["AlgorithmicEfficiencyScore"] < 100);
        Assert.True(fileResult.Metrics["ConcurrencyScore"] < 100);
        Assert.True(fileResult.Metrics["IOMaturityScore"] < 100);
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task TransactionEvaluator_flags_transaction_scopes_without_explicit_commit_or_rollback()
    {
        using var project = new TemporaryProject();
        project.Write("back/TransactionFixture.cs", """
            using System.Transactions;

            public sealed class TransactionFixture
            {
                public void Execute()
                {
                    using var scope = new TransactionScope();
                    SaveChanges();
                }

                private void SaveChanges() { }
            }
            """);

        var policy = new AegisArchitecturePolicy();
        policy.Transaction.RequireExplicitCommit = true;
        policy.Transaction.RequireRollbackOnFailure = true;

        var evaluator = new ArchitecturalTransactionEvaluator(
            NullLogger<ArchitecturalTransactionEvaluator>.Instance,
            Options.Create(policy));

        var results = (await evaluator.EvaluateAsync(project.Root, CreateContext(project))).ToArray();
        var fileResult = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("TransactionIntegrityIndex")
                && result.Metadata.GetValueOrDefault("FileName") == "TransactionFixture.cs");

        Assert.Equal("True", fileResult.Metadata["ContainsTransactionScope"]);
        Assert.Equal(60, fileResult.Metrics["CommitScore"]);
        Assert.Equal(60, fileResult.Metrics["RollbackScore"]);
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task ConfigurationEvaluator_analyzes_configuration_files_and_reports_a_summary()
    {
        using var project = new TemporaryProject();
        project.Write(
            "appsettings.json",
            """
            {
              "ConnectionStrings": {
                "Default": "Server=localhost;Password=SuperSecret123"
              },
              "Logging": {
                "LogLevel": {
                  "Default": "Information"
                }
              }
            }
            """);
        project.Write(
            "Dockerfile",
            """
            FROM example/service:latest
            RUN chmod 777 /app
            """);
        project.Write(
            "package.json",
            """
            {
              "name": "configuration-fixture",
              "dependencies": {
                "example-package": "latest"
              }
            }
            """);

        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var evaluator = GetEvaluator(scope.ServiceProvider, "ConfigurationEvaluator");

        var results = (await evaluator.EvaluateAsync(project.Root, CreateContext(project))).ToArray();
        var fileResult = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("FileName") == "appsettings.json"
                && result.Metrics.ContainsKey("ConfigurationIntegrityScore"));
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("ConfigurationFileCount"));

        Assert.True(fileResult.Metrics.ContainsKey("InfrastructureSafetyScore"));
        Assert.True(fileResult.Metrics.ContainsKey("SyntaxComplianceScore"));
        Assert.True(fileResult.Metrics.ContainsKey("ConfigurationIntegrityScore"));
        Assert.False(fileResult.Metrics.ContainsKey("SecretExposureScore"));
        Assert.False(fileResult.Metrics.ContainsKey("DependencyPinningScore"));
        Assert.False(fileResult.Metrics.ContainsKey("DependencyCount"));

        Assert.True(summary.Metrics["ConfigurationFileCount"] >= 2);
        Assert.False(summary.Metrics.ContainsKey("AverageSecretProtection"));
        Assert.False(summary.Metrics.ContainsKey("AverageDependencyDiscipline"));

        Assert.DoesNotContain(results, result =>
            result.Metadata.Keys.Any(key =>
                key.StartsWith("Finding.CFG-SECRET-", StringComparison.Ordinal)
                || key.StartsWith("Finding.CFG-DEPENDENCY-", StringComparison.Ordinal)));
        AssertAllMetricsAreFinite(results);
    }

    [Fact]
    public async Task ConfigurationEvaluator_counts_malformed_json_as_a_diagnostic()
    {
        using var project = new TemporaryProject();
        project.Write("appsettings.json", """{"Logging": {"LogLevel": }""");

        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var evaluator = GetEvaluator(scope.ServiceProvider, "ConfigurationEvaluator");

        var results = (await evaluator.EvaluateAsync(
            project.Root,
            CreateContext(project))).ToArray();

        var fileResult = Assert.Single(
            results,
            result => result.Metadata.GetValueOrDefault("FileName") == "appsettings.json"
                && result.Metrics.ContainsKey("SyntaxComplianceScore"));
        var summary = Assert.Single(
            results,
            result => result.Metrics.ContainsKey("ConfigurationFileCount"));

        Assert.Equal("Invalid", fileResult.Metadata["SyntaxValidation"]);
        Assert.Contains(
            fileResult.Metadata.Keys,
            key => key.StartsWith("Finding.CFG-SYNTAX-001", StringComparison.Ordinal));
        Assert.Equal(1, summary.Metrics["DiagnosticCount"]);
        Assert.Equal("CompletedWithDiagnostics", summary.Metadata["AnalysisStatus"]);
        Assert.Contains("appsettings.json", summary.Metadata["Diagnostics"]);
    }

    private static ServiceProvider CreateProvider(
        Action<AegisArchitecturePolicy>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddAegisCore();

        if (configure is not null)
            services.Configure(configure);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static IEvaluator GetEvaluator(
        IServiceProvider services,
        string name) =>
        Assert.Single(
            services.GetServices<IEvaluator>(),
            evaluator => evaluator.Name == name);

    private static ProjectArchitectureContext CreateContext(TemporaryProject project) =>
        new()
        {
            ProjectName = "ArchitectureEvaluatorFixture",
            RootPath = project.Root,
            Language = "C#",
            Framework = "ASP.NET Core",
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
            $"AegisEvaluatorBehavior_{Guid.NewGuid():N}");

        public TemporaryProject() => Directory.CreateDirectory(Root);

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

        public string Write(string relativePath, string content)
        {
            var fullPath = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
