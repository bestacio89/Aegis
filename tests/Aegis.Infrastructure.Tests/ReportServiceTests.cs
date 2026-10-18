using Aegis.Infrastructure.Exporters;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Infrastructure.Tests;

public sealed class ReportServiceTests
{
    [Fact]
    public async Task SaveAllAsync_exports_each_format_under_the_aegis_reports_directory()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var first = new RecordingExporter("json");
            var second = new RecordingExporter("md");
            var service = new ReportService(
                [first, second],
                NullLogger<ReportService>.Instance);

            await service.SaveAllAsync(
                new AegisArchitectureReport { ProjectName = "SampleProject" },
                new ProjectArchitectureContext { Language = "C#", Framework = ".NET 10" },
                directory,
                ArchitectureReportDetailLevel.Layered,
                ReportLanguage.French);

            Assert.Equal(1, first.CallCount);
            Assert.Equal(1, second.CallCount);
            Assert.NotNull(first.OutputPath);
            Assert.NotNull(second.OutputPath);
            Assert.Contains(Path.Combine(".aegis", "reports"), first.OutputPath!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AegisReport_SampleProject_json.json", first.OutputPath!, StringComparison.Ordinal);
            Assert.Contains("AegisReport_SampleProject_md.md", second.OutputPath!, StringComparison.Ordinal);
            Assert.Equal(ArchitectureReportDetailLevel.Layered, first.DetailLevel);
            Assert.Equal(ReportLanguage.French, first.Language);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAllAsync_continues_to_other_exporters_when_one_exporter_fails()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var failing = new RecordingExporter("broken", shouldFail: true);
            var succeeding = new RecordingExporter("json");
            var service = new ReportService(
                [failing, succeeding],
                NullLogger<ReportService>.Instance);

            await service.SaveAllAsync(
                new AegisArchitectureReport { ProjectName = "SampleProject" },
                new ProjectArchitectureContext(),
                directory);

            Assert.Equal(1, failing.CallCount);
            Assert.Equal(1, succeeding.CallCount);
            Assert.NotNull(succeeding.OutputPath);
            Assert.True(File.Exists(succeeding.OutputPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"AegisReportServiceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingExporter(string format, bool shouldFail = false) : IReportExporter
    {
        public string Format { get; } = format;
        public int CallCount { get; private set; }
        public string? OutputPath { get; private set; }
        public ArchitectureReportDetailLevel DetailLevel { get; private set; }
        public ReportLanguage Language { get; private set; }

        public async Task ExportAsync(
            AegisArchitectureReport report,
            ProjectArchitectureContext context,
            string outputPath,
            ArchitectureReportDetailLevel detailLevel = ArchitectureReportDetailLevel.FullForensic,
            ReportLanguage language = ReportLanguage.English,
            CancellationToken token = default)
        {
            CallCount++;
            OutputPath = outputPath;
            DetailLevel = detailLevel;
            Language = language;

            if (shouldFail)
            {
                throw new InvalidOperationException("Synthetic exporter failure.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, "test report", token);
        }
    }
}
