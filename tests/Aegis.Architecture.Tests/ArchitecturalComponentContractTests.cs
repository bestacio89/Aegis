using System.Text.Json;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitecturalComponentContractTests
{
    [Fact]
    public void Project_context_round_trips_logical_architecture_without_replacing_existing_projections()
    {
        var context = new ProjectArchitectureContext
        {
            ProjectName = "sample",
            Language = "Java",
            ArchitectureComponents =
            [
                new ArchitectureComponentContext
                {
                    Id = "container-domain",
                    Name = "domain",
                    QualifiedName = "sample.domain",
                    Language = "Java",
                    ComponentKind = "Package",
                    ProductionSymbolCount = 4
                }
            ],
            ArchitecturalComponents =
            [
                new ArchitecturalComponentContext
                {
                    Id = "logical-domain",
                    Name = "Domain",
                    Role = "Domain",
                    CodeContainerCount = 1,
                    ProductionSymbolCount = 4
                }
            ],
            ArchitecturalComponentMemberships =
            [
                new ArchitecturalComponentMembership
                {
                    CodeComponentId = "container-domain",
                    ArchitecturalComponentId = "logical-domain",
                    ClassificationSource = ArchitectureClassificationSource.ExplicitMapping,
                    Confidence = 1.0,
                    ClassificationRuleId = "domain-package",
                    MatchedIdentity = "sample.domain",
                    Explanation = "Explicit package mapping."
                }
            ]
        };

        var serialized = JsonSerializer.Serialize(context);
        var restored = JsonSerializer.Deserialize<ProjectArchitectureContext>(serialized);

        Assert.NotNull(restored);
        Assert.Single(restored.ArchitectureComponents);
        Assert.Single(restored.ArchitecturalComponents);
        Assert.Single(restored.ArchitecturalComponentMemberships);
        Assert.Equal("sample.domain", restored.ArchitectureComponents[0].QualifiedName);
        Assert.Equal("logical-domain", restored.ArchitecturalComponentMemberships[0].ArchitecturalComponentId);
        Assert.Equal(ArchitectureClassificationSource.ExplicitMapping,
            restored.ArchitecturalComponentMemberships[0].ClassificationSource);
    }

    [Fact]
    public void Architecture_map_configuration_defaults_to_the_current_schema()
    {
        var configuration = new ArchitectureMapConfiguration();

        Assert.Equal(ArchitectureMapConfiguration.CurrentSchemaVersion, configuration.SchemaVersion);
        Assert.Empty(configuration.Mappings);
    }
}
