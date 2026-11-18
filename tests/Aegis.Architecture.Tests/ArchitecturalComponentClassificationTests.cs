using Aegis.Architecture.Analysis.Classification;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ArchitecturalComponentClassificationTests
{
    [Fact]
    public void Explicit_exact_mapping_overrides_convention_inference()
    {
        var context = CreateContext(
            CodeComponent("domain-package", "sample.domain", "Java"));

        var configuration = new ArchitectureMapConfiguration
        {
            Mappings =
            [
                Mapping("domain-exact", "core-domain", "Core Domain",
                    ArchitectureMappingMatchKind.Exact, "sample.domain")
            ]
        };

        ArchitecturalComponentClassifier.Classify(context, configuration);

        var membership = Assert.Single(context.ArchitecturalComponentMemberships);
        Assert.Equal("core-domain", membership.ArchitecturalComponentId);
        Assert.Equal(ArchitectureClassificationSource.ExplicitMapping, membership.ClassificationSource);
        Assert.Equal("domain-exact", membership.ClassificationRuleId);
        Assert.Equal("Core Domain", Assert.Single(context.ArchitecturalComponents).Name);
        Assert.Empty(context.ArchitectureClassificationDiagnostics);
    }

    [Fact]
    public void Prefix_mapping_uses_segment_boundaries_and_most_specific_match()
    {
        var context = CreateContext(
            CodeComponent("domain-package", "com.acme.domain.entity", "Java"),
            CodeComponent("domainish-package", "com.acme.domainish", "Java"));

        var configuration = new ArchitectureMapConfiguration
        {
            Mappings =
            [
                Mapping("root", "core", "Core", ArchitectureMappingMatchKind.Prefix, "com.acme"),
                Mapping("domain", "domain", "Domain", ArchitectureMappingMatchKind.Prefix, "com.acme.domain")
            ]
        };

        ArchitecturalComponentClassifier.Classify(context, configuration);

        var domainMembership = context.ArchitecturalComponentMemberships
            .Single(membership => membership.CodeComponentId == "domain-package");
        var domainishMembership = context.ArchitecturalComponentMemberships
            .Single(membership => membership.CodeComponentId == "domainish-package");

        Assert.Equal("domain", domainMembership.ArchitecturalComponentId);
        Assert.Equal("domain", domainMembership.ClassificationRuleId);
        Assert.Equal("core", domainishMembership.ArchitecturalComponentId);
        Assert.Equal("root", domainishMembership.ClassificationRuleId);
    }

    [Fact]
    public void Conflicting_equally_specific_explicit_mappings_remain_unclassified()
    {
        var context = CreateContext(
            CodeComponent("application-package", "sample.application", "Java"));

        var configuration = new ArchitectureMapConfiguration
        {
            Mappings =
            [
                Mapping("first", "application-a", "Application A",
                    ArchitectureMappingMatchKind.Exact, "sample.application"),
                Mapping("second", "application-b", "Application B",
                    ArchitectureMappingMatchKind.Exact, "sample.application")
            ]
        };

        ArchitecturalComponentClassifier.Classify(context, configuration);

        Assert.Empty(context.ArchitecturalComponents);
        var membership = Assert.Single(context.ArchitecturalComponentMemberships);
        Assert.Null(membership.ArchitecturalComponentId);
        Assert.Contains(context.ArchitectureClassificationDiagnostics,
            diagnostic => diagnostic.Code == "ArchitectureMapAmbiguousMatch");
    }

    [Fact]
    public void Module_mapping_classifies_csharp_project_container()
    {
        var context = new ProjectArchitectureContext
        {
            ProjectName = "sample",
            RootPath = "/workspace",
            ArchitectureComponents =
            [
                new ArchitectureComponentContext
                {
                    Id = "api-project",
                    Name = "Api",
                    QualifiedName = "Api",
                    Language = "C#",
                    ComponentKind = "Project",
                    BuildModuleName = "Api",
                    BuildModulePath = "/workspace/src/Api",
                    ProductionSymbolCount = 3
                }
            ]
        };

        var configuration = new ArchitectureMapConfiguration
        {
            Mappings =
            [
                Mapping("api-module", "presentation", "Presentation",
                    ArchitectureMappingMatchKind.Module, "Api")
            ]
        };

        ArchitecturalComponentClassifier.Classify(context, configuration);

        var membership = Assert.Single(context.ArchitecturalComponentMemberships);
        Assert.Equal("presentation", membership.ArchitecturalComponentId);
        Assert.Equal(ArchitectureClassificationSource.ExplicitMapping, membership.ClassificationSource);
    }

    [Fact]
    public void Convention_inference_is_conservative_and_preserves_unclassified_containers()
    {
        var context = CreateContext(
            CodeComponent("domain", "sample.domain", "Java"),
            CodeComponent("ordinary", "sample.services", "Java"),
            CodeComponent("conflict", "sample.domain.application", "Java"));

        ArchitecturalComponentClassifier.Classify(context);

        var domainMembership = context.ArchitecturalComponentMemberships
            .Single(item => item.CodeComponentId == "domain");
        Assert.Equal("Domain", context.ArchitecturalComponents
            .Single(component => component.Id == domainMembership.ArchitecturalComponentId).Role);
        Assert.StartsWith("convention:sample:java-package:", domainMembership.ArchitecturalComponentId);
        Assert.Null(context.ArchitecturalComponentMemberships.Single(item => item.CodeComponentId == "ordinary")
            .ArchitecturalComponentId);
        Assert.Null(context.ArchitecturalComponentMemberships.Single(item => item.CodeComponentId == "conflict")
            .ArchitecturalComponentId);
        Assert.Contains(context.ArchitectureClassificationDiagnostics,
            diagnostic => diagnostic.Code == "ArchitectureClassificationConflictingConventions");
    }

    [Fact]
    public void Convention_inference_keeps_same_role_in_distinct_build_modules_separate()
    {
        var context = new ProjectArchitectureContext
        {
            ProjectName = "sample",
            RootPath = "/workspace",
            ArchitectureComponents =
            [
                new ArchitectureComponentContext
                {
                    Id = "sales-domain",
                    Name = "domain",
                    QualifiedName = "sales.domain",
                    Language = "Java",
                    ComponentKind = "Package",
                    BuildModuleName = "sales",
                    BuildModulePath = "/workspace/modules/sales",
                    ProductionSymbolCount = 2
                },
                new ArchitectureComponentContext
                {
                    Id = "inventory-domain",
                    Name = "domain",
                    QualifiedName = "inventory.domain",
                    Language = "Java",
                    ComponentKind = "Package",
                    BuildModuleName = "inventory",
                    BuildModulePath = "/workspace/modules/inventory",
                    ProductionSymbolCount = 3
                }
            ]
        };

        ArchitecturalComponentClassifier.Classify(context);

        Assert.Equal(2, context.ArchitecturalComponents.Count);
        Assert.Equal(2, context.ArchitecturalComponentMemberships
            .Select(membership => membership.ArchitecturalComponentId)
            .Distinct(StringComparer.Ordinal)
            .Count());
    }


    [Fact]
    public void Java_package_taxonomy_classifies_responsibilities_without_merging_package_identities()
    {
        var context = CreateContext(
            CodeComponent("domain-entities", "com.acme.domain.entities", "Java"),
            CodeComponent("contract-dto", "com.acme.orders.contracts.dto", "Java"),
            CodeComponent("application-handlers", "com.acme.orders.application.handlers", "Java"),
            CodeComponent("application-services", "com.acme.orders.application.services", "Java"),
            CodeComponent("technical-services", "com.acme.orders.services", "Java"),
            CodeComponent("persistence", "com.acme.orders.infrastructure.persistence", "Java"),
            CodeComponent("presentation", "com.acme.orders.presentation.api", "Java"));

        ArchitecturalComponentClassifier.Classify(context);

        var expectedRoles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["domain-entities"] = "Domain",
            ["contract-dto"] = "Contracts",
            ["application-handlers"] = "Business",
            ["application-services"] = "Business",
            ["technical-services"] = "Services",
            ["persistence"] = "Infrastructure",
            ["presentation"] = "Presentation"
        };

        foreach (var expected in expectedRoles)
        {
            var membership = context.ArchitecturalComponentMemberships
                .Single(item => item.CodeComponentId == expected.Key);
            Assert.NotNull(membership.ArchitecturalComponentId);
            Assert.Equal(expected.Value, context.ArchitecturalComponents
                .Single(component => component.Id == membership.ArchitecturalComponentId).Role);
        }

        Assert.Equal(expectedRoles.Count, context.ArchitecturalComponents.Count);
        Assert.All(context.ArchitecturalComponents, component => Assert.Equal(1, component.CodeContainerCount));
        Assert.Equal(expectedRoles.Count, context.ArchitecturalComponents
            .Select(component => component.Id)
            .Distinct(StringComparer.Ordinal)
            .Count());
    }

    [Fact]
    public void Java_package_responsibility_uses_context_and_leaves_ambiguous_entities_unclassified()
    {
        var context = CreateContext(
            CodeComponent("domain-services", "com.acme.domain.services", "Java"),
            CodeComponent("infra-services", "com.acme.infrastructure.services", "Java"),
            CodeComponent("domain-entities", "com.acme.domain.entities", "Java"),
            CodeComponent("infra-entities", "com.acme.infrastructure.persistence.entities", "Java"),
            CodeComponent("application-commands", "com.acme.application.commands", "Java"),
            CodeComponent("application-handlers", "com.acme.application.handlers", "Java"),
            CodeComponent("bare-entities", "com.acme.entities", "Java"),
            CodeComponent("bare-adapters", "com.acme.adapters", "Java"),
            CodeComponent("conflicting", "com.acme.domain.infrastructure", "Java"));

        ArchitecturalComponentClassifier.Classify(context);

        Assert.Equal("Domain", GetRole(context, "domain-services"));
        Assert.Equal("Infrastructure", GetRole(context, "infra-services"));
        Assert.Equal("Domain", GetRole(context, "domain-entities"));
        Assert.Equal("Infrastructure", GetRole(context, "infra-entities"));
        Assert.Equal("Contracts", GetRole(context, "application-commands"));
        Assert.Equal("Business", GetRole(context, "application-handlers"));

        Assert.Null(GetMembership(context, "bare-entities").ArchitecturalComponentId);
        Assert.Null(GetMembership(context, "bare-adapters").ArchitecturalComponentId);
        Assert.Null(GetMembership(context, "conflicting").ArchitecturalComponentId);
    }

    [Fact]
    public void Java_package_classification_ignores_build_module_names_and_scopes_identity_to_module()
    {
        var first = new ArchitectureComponentContext
        {
            Id = "first",
            Name = "orders",
            QualifiedName = "com.acme.orders",
            Language = "Java",
            ComponentKind = "Package",
            BuildModuleName = "domain",
            BuildModulePath = "/workspace/modules/first",
            ProductionSymbolCount = 3
        };
        var second = new ArchitectureComponentContext
        {
            Id = "second",
            Name = "orders",
            QualifiedName = "com.acme.orders",
            Language = "Java",
            ComponentKind = "Package",
            BuildModuleName = "domain",
            BuildModulePath = "/workspace/modules/second",
            ProductionSymbolCount = 3
        };
        var context = CreateContext(first, second);

        ArchitecturalComponentClassifier.Classify(context);

        Assert.All(context.ArchitecturalComponentMemberships,
            membership => Assert.Null(membership.ArchitecturalComponentId));
        Assert.Empty(context.ArchitecturalComponents);
    }

    private static ArchitecturalComponentMembership GetMembership(
        ProjectArchitectureContext context,
        string codeComponentId) =>
        context.ArchitecturalComponentMemberships.Single(item => item.CodeComponentId == codeComponentId);

    private static string? GetRole(ProjectArchitectureContext context, string codeComponentId)
    {
        var membership = GetMembership(context, codeComponentId);
        return context.ArchitecturalComponents
            .SingleOrDefault(component => component.Id == membership.ArchitecturalComponentId)?.Role;
    }

    [Fact]
    public async Task Configuration_loader_reads_versioned_json_without_executing_project_content()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aegis-architecture-map-{Guid.NewGuid():N}");
        var configurationDirectory = Path.Combine(root, ".aegis");
        Directory.CreateDirectory(configurationDirectory);

        try
        {
            var path = Path.Combine(configurationDirectory, "architecture-map.json");
            await File.WriteAllTextAsync(path,
                """
                {
                  "schemaVersion": 1,
                  "mappings": [
                    {
                      "id": "domain-prefix",
                      "componentId": "domain",
                      "componentName": "Domain",
                      "matchKind": "prefix",
                      "value": "sample.domain"
                    }
                  ]
                }
                """);

            var result = await ArchitectureMapConfigurationLoader.LoadAsync(root);

            Assert.Equal(ArchitectureMapConfiguration.CurrentSchemaVersion, result.Configuration.SchemaVersion);
            Assert.Equal("domain-prefix", Assert.Single(result.Configuration.Mappings).Id);
            Assert.Empty(result.Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ProjectArchitectureContext CreateContext(params ArchitectureComponentContext[] components) =>
        new()
        {
            ProjectName = "sample",
            RootPath = "/workspace",
            ArchitectureComponents = [.. components]
        };

    private static ArchitectureComponentContext CodeComponent(
        string id,
        string qualifiedName,
        string language) =>
        new()
        {
            Id = id,
            Name = qualifiedName.Split('.').Last(),
            QualifiedName = qualifiedName,
            Language = language,
            ComponentKind = "Package",
            BuildModuleName = "sample",
            BuildModulePath = "/workspace",
            ProductionSymbolCount = 3
        };

    private static ArchitectureComponentMapping Mapping(
        string id,
        string componentId,
        string componentName,
        ArchitectureMappingMatchKind matchKind,
        string value) =>
        new()
        {
            Id = id,
            ComponentId = componentId,
            ComponentName = componentName,
            MatchKind = matchKind,
            Value = value
        };
}
