using Aegis.Architecture.Evaluators.Infrastructure;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ConfigurationEvaluatorSyntaxRegressionTests
{
    [Fact]
    public async Task Json_with_comments_and_trailing_commas_is_valid_configuration()
    {
        using var project = new TemporaryProject();
        project.Write("appsettings.json", """
            {
              // Supported by the .NET configuration JSON parser.
              "Logging": {
                "LogLevel": {
                  "Default": "Information",
                },
              },
            }
            """);

        var results = await EvaluateAsync(project.Root);
        var file = Assert.Single(results, result =>
            result.Metadata.GetValueOrDefault("FileName") == "appsettings.json");

        Assert.Equal("Valid", file.Metadata["SyntaxValidation"]);
        Assert.DoesNotContain("CFG-SYNTAX-001", file.Metadata.Keys);
        Assert.Equal(100, file.Metrics["SyntaxComplianceScore"]);
    }

    [Fact]
    public async Task Valid_yaml_with_comments_and_quoted_colons_is_valid()
    {
        using var project = new TemporaryProject();
        project.Write(".github/workflows/ci.yml", """
            name: CI
            on:
              pull_request:
                branches: [develop]
            jobs:
              test:
                runs-on: ubuntu-latest
                steps:
                  - name: Print URL
                    run: "echo https://example.test:8443/health"
            """);

        var results = await EvaluateAsync(project.Root);
        var file = Assert.Single(results, result =>
            result.Metadata.GetValueOrDefault("FileName") == "ci.yml");

        Assert.Equal("Valid", file.Metadata["SyntaxValidation"]);
        Assert.False(file.Metadata.ContainsKey("SyntaxDiagnostic"));
    }

    [Fact]
    public async Task Invalid_json_and_yaml_are_reported_with_parser_diagnostics_without_aborting_scan()
    {
        using var project = new TemporaryProject();
        project.Write("broken.json", """
            {
              "Logging": {
                "Default": "Information"
                "Extra": true
              }
            }
            """);
        project.Write("broken.yml", """
            service:
              image: nginx
                invalid: true
            """);
        project.Write("valid.json", """{ "enabled": true }""");

        var results = await EvaluateAsync(project.Root);

        var invalidJson = Assert.Single(results, result =>
            result.Metadata.GetValueOrDefault("FileName") == "broken.json");
        var invalidYaml = Assert.Single(results, result =>
            result.Metadata.GetValueOrDefault("FileName") == "broken.yml");
        var validJson = Assert.Single(results, result =>
            result.Metadata.GetValueOrDefault("FileName") == "valid.json");

        Assert.Equal("Invalid", invalidJson.Metadata["SyntaxValidation"]);
        Assert.Contains("JSON syntax validation failed", invalidJson.Metadata["SyntaxDiagnostic"]);
        Assert.Contains(invalidJson.Metadata, pair => pair.Key.StartsWith("Finding.CFG-SYNTAX-001", StringComparison.Ordinal));
        Assert.Equal("Invalid", invalidYaml.Metadata["SyntaxValidation"]);
        Assert.Contains("YAML syntax validation failed", invalidYaml.Metadata["SyntaxDiagnostic"]);
        Assert.Equal("Valid", validJson.Metadata["SyntaxValidation"]);
        Assert.Equal(0, invalidJson.Metrics["SyntaxComplianceScore"]);
        Assert.Equal(0, invalidYaml.Metrics["SyntaxComplianceScore"]);
    }

    private static async Task<ArchitectureEvaluatorResult[]> EvaluateAsync(string projectPath)
    {
        var evaluator = new ConfigurationEvaluator(
            NullLogger<ConfigurationEvaluator>.Instance,
            Options.Create(new AegisArchitecturePolicy()));

        return (await evaluator.EvaluateAsync(
            projectPath,
            new ProjectArchitectureContext
            {
                ProjectName = "ConfigurationSyntaxFixture",
                RootPath = projectPath,
                Language = "C#",
                Framework = "ASP.NET Core",
                DetectedFrameworks = ["ASP.NET Core"]
            })).ToArray();
    }

    private sealed class TemporaryProject : IDisposable
    {
        public TemporaryProject()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "Aegis-Configuration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string relativePath, string content)
        {
            var path = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A test runner may briefly retain a file handle.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup failure must not hide the assertion failure.
            }
        }
    }
}
