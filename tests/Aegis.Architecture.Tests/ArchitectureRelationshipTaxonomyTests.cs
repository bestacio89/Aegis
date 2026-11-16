using Aegis.Shared.Architecture.Enums;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitectureRelationshipTaxonomyTests
{
    [Fact]
    public void Taxonomy_keeps_build_native_api_messaging_rpc_and_data_relationships_distinct()
    {
        Assert.Equal(100, (int)ArchitectureRelationshipKind.BuildProjectReference);
        Assert.Equal(101, (int)ArchitectureRelationshipKind.BuildModuleDependency);
        Assert.Equal(102, (int)ArchitectureRelationshipKind.BuildPackageDependency);

        Assert.Equal(200, (int)ArchitectureRelationshipKind.Declares);
        Assert.Equal(203, (int)ArchitectureRelationshipKind.Calls);
        Assert.Equal(204, (int)ArchitectureRelationshipKind.References);
        Assert.Equal(209, (int)ArchitectureRelationshipKind.UsesAnnotation);

        Assert.Equal(300, (int)ArchitectureRelationshipKind.HttpRequest);
        Assert.Equal(301, (int)ArchitectureRelationshipKind.HttpRoute);

        Assert.Equal(400, (int)ArchitectureRelationshipKind.MessagePublish);
        Assert.Equal(401, (int)ArchitectureRelationshipKind.MessageConsume);

        Assert.Equal(500, (int)ArchitectureRelationshipKind.RpcCall);
        Assert.Equal(501, (int)ArchitectureRelationshipKind.RpcEndpoint);

        Assert.Equal(600, (int)ArchitectureRelationshipKind.DataRead);
        Assert.Equal(601, (int)ArchitectureRelationshipKind.DataWrite);
        Assert.Equal(602, (int)ArchitectureRelationshipKind.DataMapping);
    }

    [Fact]
    public void Taxonomy_does_not_use_generic_dependency_as_a_semantic_relationship_kind()
    {
        Assert.DoesNotContain(
            Enum.GetNames<ArchitectureRelationshipKind>(),
            name => string.Equals(name, "DependsOn", StringComparison.Ordinal));
    }
}
