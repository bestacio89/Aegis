using Aegis.Architecture.RuleEngines;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class RuleEngineScopeTests
{
    [Fact]
    public void EvaluateDetailed_matches_facts_only_to_rules_with_the_same_scope()
    {
        var engine = new RuleEngineCore(
            NullLogger<RuleEngineCore>.Instance,
            new AegisArchitecturePolicy());

        var projectFact = new ArchitectureEvaluatorResult(
            "ArchitecturalEvaluator",
            "SampleProject")
        {
            Category = "ArchitectureSummary",
            Metrics = new Dictionary<string, double>
            {
                ["CircularDependencyCount"] = 1
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "Project"
            }
        };

        var fileFact = new ArchitectureEvaluatorResult(
            "NamingEvaluator",
            "Widget.cs")
        {
            Metrics = new Dictionary<string, double>
            {
                // A project metric on a file fact must not trigger a project rule.
                ["CircularDependencyCount"] = 99,
                ["TypeNamingCompliance"] = 20
            },
            Metadata = new Dictionary<string, string>
            {
                ["EvaluationScope"] = "File",
                ["FilePath"] = "src/Widget.cs"
            }
        };

        var outcome = engine.EvaluateDetailed(
            new ProjectArchitectureContext(),
            [projectFact, fileFact]);

        Assert.Contains(
            outcome.Evaluations,
            evaluation =>
                evaluation.RuleId == "AEG-ARCH-R1"
                && evaluation.Scope == "Project"
                && evaluation.Target == "SampleProject"
                && evaluation.ObservedValue == 1);

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation =>
                evaluation.RuleId == "AEG-ARCH-R1"
                && evaluation.Scope == "File");

        Assert.Contains(
            outcome.Evaluations,
            evaluation =>
                evaluation.RuleId == "AEG-NAM-R1"
                && evaluation.Scope == "File"
                && evaluation.Target == "Widget.cs"
                && evaluation.ObservedValue == 20);

        Assert.DoesNotContain(
            outcome.Evaluations,
            evaluation =>
                evaluation.RuleId == "AEG-NAM-R1"
                && evaluation.Scope == "Project");
    }
}
