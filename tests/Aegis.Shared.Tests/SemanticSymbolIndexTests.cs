using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Shared.Tests;

public sealed class SemanticSymbolIndexTests
{
    [Fact]
    public void Index_preserves_duplicate_provider_ids_across_languages_and_projects()
    {
        var runs = new[]
        {
            CreateRun("java", "Java", "/workspace/java",
                CreateSymbol("same-id", "sample.Order", "Java", SemanticSymbolOrigin.Source)),
            CreateRun("kotlin", "Kotlin", "/workspace/kotlin",
                CreateSymbol("same-id", "sample.Order", "Kotlin", SemanticSymbolOrigin.Source))
        };

        var index = SemanticSymbolIndex.Create(runs);

        Assert.Equal(2, index.Count);
        Assert.Equal(2, index.FindByProviderId("same-id").Count);
        Assert.Empty(index.FindByQualifiedName("C#", "sample.Order"));
        Assert.Single(index.FindByQualifiedName("Java", "sample.Order"));
        Assert.Single(index.FindByOwnerProjectPath("/workspace/java"));
        Assert.NotEqual(
            index.FindByProviderId("same-id")[0].Identity,
            index.FindByProviderId("same-id")[1].Identity);
    }

    [Fact]
    public void Index_preserves_duplicate_occurrences_and_exact_run_symbol_positions()
    {
        var symbol = CreateSymbol("duplicate", "sample.Type", "Java", SemanticSymbolOrigin.Source);
        var runs = new[]
        {
            CreateRun("run-a", "Java", "/workspace/a", symbol, symbol),
            CreateRun("run-b", "Java", "/workspace/a", symbol)
        };

        var index = SemanticSymbolIndex.Create(runs);

        Assert.Equal(3, index.Count);
        Assert.Equal([0, 1], index.ForRun(0).Select(item => item.SymbolIndex).ToArray());
        Assert.Equal(1, index.ForRun(1)[0].RunIndex);
        Assert.Equal(1, index.ForRun(1)[0].SymbolIndex);
        Assert.Equal(3, index.FindByIdentity(index.Occurrences[0].Identity!).Count);
    }

    [Fact]
    public void External_symbols_require_explicit_owner_and_never_inherit_consumer_path()
    {
        var run = CreateRun(
            "consumer",
            "Java",
            "/workspace/consumer",
            CreateSymbol("external-no-owner", "library.Type", "Java", SemanticSymbolOrigin.External),
            new SemanticSymbol
            {
                Id = "external-owned",
                QualifiedName = "library.OwnedType",
                Language = "Java",
                Origin = SemanticSymbolOrigin.External,
                OwningProjectPath = "/workspace/library"
            });

        var index = SemanticSymbolIndex.Create([run]);

        Assert.Null(index.ForRun(0)[0].Identity);
        Assert.Null(index.ForRun(0)[0].OwnerProjectPath);
        Assert.Equal("/workspace/library", index.ForRun(0)[1].Identity!.OwnerProjectPath);
        Assert.Empty(index.FindByOwnerProjectPath("/workspace/consumer")
            .Where(item => item.Symbol.Origin == SemanticSymbolOrigin.External));
        Assert.Single(index.FindByOwnerProjectPath("/workspace/library"));
    }

    [Fact]
    public void Index_retains_symbols_from_failed_runs_and_ownerless_or_invalid_declarations()
    {
        var failed = CreateRun(
            "failed",
            "Java",
            "/workspace/failed",
            new SemanticSymbol { Id = "", QualifiedName = "sample.NoId", Origin = SemanticSymbolOrigin.Source },
            new SemanticSymbol { Id = "invalid-owner", QualifiedName = "sample.External", Origin = SemanticSymbolOrigin.External,
                OwningProjectPath = "relative/path" });
        failed = new SemanticAnalysisRun
        {
            RunId = failed.RunId,
            Language = failed.Language,
            EngineId = failed.EngineId,
            ProjectPath = failed.ProjectPath,
            Status = SemanticAnalysisStatus.Failed,
            Symbols = failed.Symbols
        };

        var index = SemanticSymbolIndex.Create([failed]);

        Assert.Equal(2, index.Count);
        Assert.Equal(SemanticAnalysisStatus.Failed, index.ForRun(0)[0].Run.Status);
        Assert.Null(index.ForRun(0)[0].Identity);
        Assert.Null(index.ForRun(0)[1].Identity);
        Assert.Null(index.ForRun(0)[1].OwnerProjectPath);
    }

    [Fact]
    public void Index_handles_empty_runs_and_language_scoped_qualified_names()
    {
        var index = SemanticSymbolIndex.Create(
        [
            CreateRun("empty", "Java", "/workspace/empty"),
            CreateRun("java", "Java", "/workspace/java",
                CreateSymbol("java-type", "same.Name", "Java", SemanticSymbolOrigin.Source)),
            CreateRun("ts", "TypeScript", "/workspace/ts",
                CreateSymbol("ts-type", "same.Name", "TypeScript", SemanticSymbolOrigin.Source))
        ]);

        Assert.Empty(index.ForRun(0));
        Assert.Empty(index.FindByProviderId("missing"));
        Assert.Single(index.FindByQualifiedName("Java", "same.Name"));
        Assert.Single(index.FindByQualifiedName("TypeScript", "same.Name"));
        Assert.Empty(index.FindByQualifiedName("Kotlin", "same.Name"));
    }

    private static SemanticAnalysisRun CreateRun(
        string runId,
        string language,
        string projectPath,
        params SemanticSymbol[] symbols) =>
        new()
        {
            RunId = runId,
            Language = language,
            EngineId = "test-provider",
            ProjectPath = projectPath,
            Status = SemanticAnalysisStatus.Completed,
            Symbols = [.. symbols]
        };

    private static SemanticSymbol CreateSymbol(
        string id,
        string qualifiedName,
        string language,
        SemanticSymbolOrigin origin) =>
        new()
        {
            Id = id,
            QualifiedName = qualifiedName,
            Language = language,
            Origin = origin,
            ContainerName = "sample",
            ContainerKind = "Package",
            FilePath = origin == SemanticSymbolOrigin.Source ? "/workspace/source.java" : null
        };
}
