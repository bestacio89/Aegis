using System.IO;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Wpf.ViewModels.Architecture;
using FluentAssertions;
using Xunit;

namespace Aegis.Wpf.Tests;

public sealed class SystemLandscapeEvidenceResolverTests
{
    [Fact]
    public void Resolve_UsesExactIntegrationOccurrenceInsteadOfProviderIdLookup()
    {
        var first = new MessageIntegrationEvidence
        {
            EvidenceId = "duplicate", ProviderId = "provider", ModulePath = "src/A",
            SourceFile = "src/A/Publisher.cs", DeclarationEvidence = "Publish order"
        };
        var second = new MessageIntegrationEvidence
        {
            EvidenceId = "duplicate", ProviderId = "provider", ModulePath = "src/B",
            SourceFile = "src/B/Publisher.cs", DeclarationEvidence = "Publish invoice"
        };
        var context = new ProjectArchitectureContext
        {
            RootPath = Path.GetTempPath(),
            MessageIntegrationEvidenceRegistry = new IntegrationEvidenceRegistry<MessageIntegrationEvidence>([first, second])
        };
        var reference = new ArchitectureGraphEvidenceReference
        {
            Domain = ArchitectureGraphEvidenceDomain.Messaging, OccurrenceIndex = 1,
            EvidenceId = "duplicate", ProviderId = "provider"
        };

        var result = new SystemLandscapeEvidenceResolver().Resolve(context, reference);

        result.Details.Should().Contain("src/B");
        result.Details.Should().Contain("Publish invoice");
        result.Identity.Should().Contain("Occurrence 1");
    }

    [Fact]
    public void Resolve_RejectsMismatchedIdentityWithoutFallingBackToAnotherOccurrence()
    {
        var context = new ProjectArchitectureContext
        {
            Dependencies = [new ArchitectureDependencyContext
            {
                Source = "A", Target = "B", DependencyType = "ProjectReference"
            }]
        };
        var reference = new ArchitectureGraphEvidenceReference
        {
            Domain = ArchitectureGraphEvidenceDomain.BuildDependency, OccurrenceIndex = 0,
            EvidenceId = "stale-evidence-id", ProviderId = "ProjectReference"
        };

        var result = new SystemLandscapeEvidenceResolver().Resolve(context, reference);

        result.Details.Should().Contain("absent or its identity does not match");
        result.HasSourceLocation.Should().BeFalse();
    }

    [Fact]
    public void Resolve_RejectsOutOfRangeOccurrence()
    {
        var context = new ProjectArchitectureContext
        {
            MessageIntegrationEvidenceRegistry = new IntegrationEvidenceRegistry<MessageIntegrationEvidence>([])
        };
        var reference = new ArchitectureGraphEvidenceReference
        {
            Domain = ArchitectureGraphEvidenceDomain.Messaging, OccurrenceIndex = 4
        };

        var result = new SystemLandscapeEvidenceResolver().Resolve(context, reference);

        result.Details.Should().Contain("absent or its identity does not match");
        result.HasSourceLocation.Should().BeFalse();
    }

    [Fact]
    public void TryResolveSourcePath_RejectsPathsOutsideRepositoryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outside = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cs");
        File.WriteAllText(outside, "class Outside { }");
        try
        {
            SystemLandscapeEvidenceResolver.TryResolveSourcePath(root, outside, out _).Should().BeFalse();
        }
        finally
        {
            File.Delete(outside);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void TryResolveSourcePath_RequiresAnExistingFileInsideRepositoryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "Example.cs");
        File.WriteAllText(source, "class Example { }");
        try
        {
            SystemLandscapeEvidenceResolver.TryResolveSourcePath(root, "Example.cs", out var resolved).Should().BeTrue();
            resolved.Should().Be(Path.GetFullPath(source));
        }
        finally
        {
            File.Delete(source);
            Directory.Delete(root);
        }
    }
}
