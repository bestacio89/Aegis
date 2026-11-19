using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Xunit;

namespace Aegis.Architecture.Tests;

public sealed class ApiClientRequestDiscoveryTests
{
    [Fact]
    public void RegistryPreservesDuplicateRequestOccurrencesAndDynamicEvidence()
    {
        var first = new ApiClientRequestEvidence
        {
            EvidenceId = "first",
            ModulePath = "/repo/frontend",
            HttpMethod = "GET",
            RequestUriExpression = "/orders",
            ResolutionStatus = ApiClientRequestResolutionStatus.Resolved
        };
        var second = new ApiClientRequestEvidence
        {
            EvidenceId = "second",
            ModulePath = "/repo/frontend",
            HttpMethod = "GET",
            RequestUriExpression = "ordersUrl",
            ResolutionStatus = ApiClientRequestResolutionStatus.Dynamic
        };

        var registry = new ApiClientRequestDiscoveryRegistry([first, second]);

        Assert.Equal(2, registry.Count);
        Assert.Equal(2, registry.ForModule("/repo/frontend").Count);
        Assert.Single(registry.ForStatus(ApiClientRequestResolutionStatus.Dynamic));
        Assert.Same(first, registry.Evidence[0]);
        Assert.Same(second, registry.Evidence[1]);
    }

    [Fact]
    public void RegistrySnapshotsInputAndExposesReadOnlyCollections()
    {
        var evidence = new List<ApiClientRequestEvidence>
        {
            new() { EvidenceId = "request", ModulePath = "/repo/api" }
        };

        var registry = new ApiClientRequestDiscoveryRegistry(evidence);
        evidence.Clear();

        Assert.Single(registry.Evidence);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ApiClientRequestEvidence>)registry.Evidence).Add(new ApiClientRequestEvidence()));
    }

    [Fact]
    public void RegistryRejectsNullEvidenceCollection()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ApiClientRequestDiscoveryRegistry(null!));
    }
}
