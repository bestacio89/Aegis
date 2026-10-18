using Aegis.Architecture.Evaluators.Infrastructure;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class LoggingEvaluatorScopeTests
{
    [Fact]
    public async Task Empty_logging_summary_is_project_scoped_without_duplicate_metadata()
    {
        using var directory = Directory.CreateTempSubdirectory();
        var evaluator = new LoggingEvaluator(
            NullLogger<LoggingEvaluator>.Instance,
            Options.Create(new AegisArchitecturePolicy()));

        var results = (await evaluator.EvaluateAsync(
            directory.FullName,
            new ProjectArchitectureContext())).ToArray();

        var summary = Assert.Single(results);
        Assert.Equal("Project", summary.Metadata["EvaluationScope"]);
        Assert.Equal("InfrastructureSummary", summary.Category);
    }
}
