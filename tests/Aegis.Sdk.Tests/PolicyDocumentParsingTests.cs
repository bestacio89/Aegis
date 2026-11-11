using System.Text.Json;
using Aegis.Sdk;
using Xunit;

namespace Aegis.Sdk.Tests;

public sealed class PolicyDocumentParsingTests
{
    [Theory]
    [InlineData("""
        {
          "AegisPolicy": {
            "Architecture": {
              "DesignPatterns": {
                "EnforceCommandPattern": true,
                "EnforceStrategyPattern": true,
                "EnforceObserverPattern": true
              }
            }
          }
        }
        """)]
    [InlineData("""
        {
          "AegisArchitecturePolicy": {
            "Architecture": {
              "DesignPatterns": {
                "EnforceCommandPattern": true,
                "EnforceStrategyPattern": true,
                "EnforceObserverPattern": true
              }
            }
          }
        }
        """)]
    [InlineData("""
        {
          "Architecture": {
            "DesignPatterns": {
              "EnforceCommandPattern": true,
              "EnforceStrategyPattern": true,
              "EnforceObserverPattern": true
            }
          }
        }
        """)]
    public void DeserializePolicyDocument_applies_pattern_settings_for_supported_document_shapes(
        string json)
    {
        var policy = AegisArchitectureAnalysisRunner.DeserializePolicyDocument(json);

        Assert.True(policy.Architecture.DesignPatterns.EnforceCommandPattern);
        Assert.True(policy.Architecture.DesignPatterns.EnforceStrategyPattern);
        Assert.True(policy.Architecture.DesignPatterns.EnforceObserverPattern);
    }

    [Fact]
    public void DeserializePolicyDocument_rejects_ambiguous_wrapper_documents()
    {
        const string json = """
            {
              "AegisPolicy": {},
              "AegisArchitecturePolicy": {}
            }
            """;

        Assert.Throws<JsonException>(
            () => AegisArchitectureAnalysisRunner.DeserializePolicyDocument(json));
    }
}
