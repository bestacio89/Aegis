using Aegis.Architecture.Evaluators.DesignPatterns;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class DecoratorPatternEvaluatorTests
{
    [Fact]
    public async Task Structural_wrapper_without_decorator_intent_is_not_a_naming_violation()
    {
        var directory = Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../")),
            $"AegisDecoratorFixture_{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(directory, "back");
        Directory.CreateDirectory(sourceDirectory);

        try
        {
            var file = Path.Combine(sourceDirectory, "ServiceWrapper.cs");
            await File.WriteAllTextAsync(
                file,
                """
                public interface IService
                {
                    void Execute();
                }

                public sealed class ServiceWrapper : IService
                {
                    private readonly IService _service;

                    public ServiceWrapper(IService service)
                    {
                        _service = service;
                    }

                    public void Execute()
                    {
                        _service.Execute();
                    }
                }
                """);

            var evaluator = new DecoratorPatternEvaluator(
                NullLogger<DecoratorPatternEvaluator>.Instance,
                Options.Create(new AegisArchitecturePolicy()));

            var results = (await evaluator.EvaluateAsync(
                directory,
                new ProjectArchitectureContext())).ToArray();

            var classResult = Assert.Single(
                results,
                result => result.Metadata.GetValueOrDefault("ClassName") == "ServiceWrapper");

            Assert.Equal("True", classResult.Metadata["DecoratorCandidate"]);
            Assert.Equal(
                "NotApplicable",
                classResult.Metadata["NamingConventionCompliant"]);
            Assert.False(classResult.Metrics.ContainsKey("DecoratorNamingCompliance"));
            Assert.False(classResult.Metrics.ContainsKey("DecoratorComplianceScore"));
            Assert.True(classResult.Metrics.ContainsKey("DecoratorDelegationComplianceScore"));
            Assert.Equal(
                classResult.Metrics["StructuralComplianceScore"],
                classResult.Metrics["DecoratorDelegationComplianceScore"]);
            Assert.True(classResult.Metrics.ContainsKey("StructuralComplianceScore"));

            var summary = Assert.Single(
                results,
                result => result.Metadata.GetValueOrDefault("EvaluationScope") == "Project");

            Assert.Equal(1d, summary.Metrics["DecoratorCandidateCount"]);
            Assert.Equal(1d, summary.Metrics["StructurallySupportedCandidateCount"]);
            Assert.Equal(0d, summary.Metrics["DecoratorNamingApplicableCount"]);
            Assert.Equal(0d, summary.Metrics["DecoratorNamingViolationCount"]);
            Assert.False(summary.Metrics.ContainsKey("DecoratorNamingCompliance"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
