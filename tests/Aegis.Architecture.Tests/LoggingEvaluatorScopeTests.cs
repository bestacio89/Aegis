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
    public async Task Logging_inventory_uses_shared_generated_and_vendored_exclusions()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            // LoggingEvaluator applies the same application-source boundary as
            // the other architecture evaluators. Keep all candidates beneath
            // that boundary so the assertions exercise exclusions, not discovery.
            var application = Path.Combine(directory.FullName, "back");
            Directory.CreateDirectory(application);
            File.WriteAllText(Path.Combine(application, "Application.cs"), "public class Application { }");

            var generated = Path.Combine(application, "Generated");
            Directory.CreateDirectory(generated);
            File.WriteAllText(Path.Combine(generated, "Generated.cs"), "public class Generated { }");

            var thirdParty = Path.Combine(application, "ThirdParty");
            Directory.CreateDirectory(thirdParty);
            File.WriteAllText(Path.Combine(thirdParty, "Vendor.cs"), "public class Vendor { }");

            var textMeshProExamples = Path.Combine(
                application,
                "TextMesh Pro",
                "Examples & Extras");
            Directory.CreateDirectory(textMeshProExamples);
            File.WriteAllText(
                Path.Combine(textMeshProExamples, "TMP_DigitValidator.cs"),
                "public class TMP_DigitValidator { }");

            var evaluator = new LoggingEvaluator(
                NullLogger<LoggingEvaluator>.Instance,
                Options.Create(new AegisArchitecturePolicy()));

            var results = (await evaluator.EvaluateAsync(
                directory.FullName,
                new ProjectArchitectureContext())).ToArray();

            var summary = Assert.Single(results);
            Assert.Equal(1, summary.Metrics["SourceFilesScanned"]);
            Assert.Equal(0, summary.Metrics["DiagnosticCount"]);
            Assert.Equal("Completed", summary.Metadata["AnalysisStatus"]);
            Assert.Equal("True", summary.Metadata["AnalysisComplete"]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Empty_logging_summary_is_project_scoped_without_duplicate_metadata()
    {
        var directory = Directory.CreateTempSubdirectory();

        try
        {
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
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
