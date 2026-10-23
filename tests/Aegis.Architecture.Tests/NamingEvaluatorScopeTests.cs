using Aegis.Architecture.Evaluators.Naming;
using Aegis.Architecture.Evaluators.Naming.Analysis;
using Aegis.Architecture.Evaluators.Naming.Policies;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Architecture.Models.Policies;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class NamingEvaluatorScopeTests
{
    [Fact]
    public async Task EvaluateAsync_excludes_vendored_TextMeshPro_examples_without_excluding_first_party_folders()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"AegisNamingScopeTests_{Guid.NewGuid():N}");

        try
        {
            var vendoredExample = Path.Combine(
                root,
                "Assets",
                "TextMesh Pro",
                "Examples & Extras",
                "Scripts",
                "Benchmark01.cs");
            var firstPartyHud = Path.Combine(
                root,
                "Assets",
                "TextMeshPro",
                "Custom",
                "HudLabel.cs");
            var firstPartyExample = Path.Combine(
                root,
                "Assets",
                "Examples & Extras",
                "GameOwnedExample.cs");

            Directory.CreateDirectory(Path.GetDirectoryName(vendoredExample)!);
            Directory.CreateDirectory(Path.GetDirectoryName(firstPartyHud)!);
            Directory.CreateDirectory(Path.GetDirectoryName(firstPartyExample)!);

            File.WriteAllText(vendoredExample, "public sealed class Benchmark01 { }");
            File.WriteAllText(firstPartyHud, "public sealed class HudLabel { }");
            File.WriteAllText(firstPartyExample, "public sealed class GameOwnedExample { }");

            var evaluator = new NamingEvaluator(
                NullLogger<NamingEvaluator>.Instance,
                Options.Create(new AegisArchitecturePolicy()),
                new NamingConventionResolver(),
                new CSharpNamingDeclarationAnalyzer());

            var context = new ProjectArchitectureContext
            {
                RootPath = root,
                Language = "C#",
                Framework = "Unity"
            };

            var results = (await evaluator.EvaluateAsync(
                root,
                context,
                CancellationToken.None)).ToList();

            Assert.Contains(results, result =>
                result.Target.EndsWith("HudLabel.cs", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(results, result =>
                result.Target.EndsWith("GameOwnedExample.cs", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(results, result =>
                result.Target.EndsWith("Benchmark01.cs", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
