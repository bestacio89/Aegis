using System.Text.Json;
using Aegis.Infrastructure.Exporters;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Infrastructure.Tests;

public sealed class JsonReportExporterTests
{
    [Theory]
    [InlineData(ArchitectureReportDetailLevel.SummaryOnly, false)]
    [InlineData(ArchitectureReportDetailLevel.Layered, false)]
    [InlineData(ArchitectureReportDetailLevel.FullForensic, true)]
    public async Task ExportAsync_emits_the_requested_detail_level(
        ArchitectureReportDetailLevel detailLevel,
        bool includesRuleIdentity)
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var outputPath = Path.Combine(directory, "nested", "report.json");
            var exporter = new JsonReportExporter(NullLogger<JsonReportExporter>.Instance);

            await exporter.ExportAsync(
                CreateReport(),
                CreateContext(),
                outputPath,
                detailLevel);

            Assert.True(File.Exists(outputPath));

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            var root = document.RootElement;

            Assert.Equal("SampleProject", root.GetProperty("Report").GetProperty("ProjectName").GetString());
            Assert.Equal(detailLevel.ToString(), root.GetProperty("Overview").GetProperty("Mode").GetString());

            var results = root.GetProperty("Results");
            Assert.Equal(JsonValueKind.Array, results.ValueKind);
            Assert.NotEmpty(results.EnumerateArray());

            if (includesRuleIdentity)
            {
                Assert.Equal("AEG-TEST-001", results[0].GetProperty("RuleId").GetString());
                Assert.Equal("Example violation", results[0].GetProperty("Message").GetString());
            }
            else if (detailLevel == ArchitectureReportDetailLevel.Layered)
            {
                Assert.True(results[0].TryGetProperty("Domain", out _));
                Assert.False(results[0].TryGetProperty("RuleId", out _));
            }
            else
            {
                Assert.True(results[0].TryGetProperty("Category", out _));
                Assert.True(results[0].TryGetProperty("Violations", out _));
                Assert.False(results[0].TryGetProperty("RuleId", out _));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAsync_creates_missing_parent_directories()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var outputPath = Path.Combine(directory, "one", "two", "report.json");
            var exporter = new JsonReportExporter(NullLogger<JsonReportExporter>.Instance);

            await exporter.ExportAsync(CreateReport(), CreateContext(), outputPath);

            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAsync_honors_cancellation()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var exporter = new JsonReportExporter(NullLogger<JsonReportExporter>.Instance);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                exporter.ExportAsync(
                    CreateReport(),
                    CreateContext(),
                    Path.Combine(directory, "report.json"),
                    token: cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AegisArchitectureReport CreateReport()
    {
        var report = new AegisArchitectureReport
        {
            ProjectName = "SampleProject",
            ProjectPath = "/repo/SampleProject",
            Language = "C#",
            Framework = ".NET 10",
            TotalFilesScanned = 12
        };

        report.Results.Add(new ArchitectureRuleresult(
            "AEG-TEST-001",
            "Example rule",
            ArchitectureRuleCategory.General,
            ArchitectureRuleSeverity.High,
            "src/Example.cs",
            "Sample",
            "Example violation",
            DateTimeOffset.UtcNow)
        {
            Domain = "Backend",
            WeightedImpact = 0.8,
            ImpactScore = 0.2,
            Recommendation = "Correct the example violation."
        });

        return report;
    }

    private static ProjectArchitectureContext CreateContext() => new()
    {
        ProjectName = "SampleProject",
        Language = "C#",
        Framework = ".NET 10",
        ArchitectureStyle = "Layered",
        FileCount = 12,
        LinesOfCode = 300
    };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AegisInfrastructureTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
