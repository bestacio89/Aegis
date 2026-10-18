using Aegis.Shared.Security.Enums;
using Aegis.Shared.Security.Models;
using Xunit;

namespace Aegis.Security.Tests;

public sealed class SecurityModeCatalogTests
{
    [Theory]
    [InlineData("Cloud")]
    [InlineData("Crypto")]
    [InlineData("Network")]
    [InlineData("OS")]
    [InlineData("TLS")]
    public void Runtime_observation_domains_are_live(string domain)
    {
        Assert.Equal(ProbeMode.Live, SecurityModeCatalog.ModeOfDomain(domain));
    }

    [Theory]
    [InlineData("cloud")]
    [InlineData("NETWORK")]
    [InlineData("Tls")]
    public void Domain_classification_is_case_insensitive(string domain)
    {
        Assert.Equal(ProbeMode.Live, SecurityModeCatalog.ModeOfDomain(domain));
    }

    [Theory]
    [InlineData("Secrets")]
    [InlineData("Application")]
    [InlineData("WCF")]
    [InlineData("Governance")]
    [InlineData("UnknownFutureDomain")]
    public void Project_analysis_domains_default_to_static(string domain)
    {
        Assert.Equal(ProbeMode.Static, SecurityModeCatalog.ModeOfDomain(domain));
    }

    [Fact]
    public void Network_port_probe_is_live_but_unknown_probes_default_to_static()
    {
        Assert.Equal(ProbeMode.Live, SecurityModeCatalog.ModeOfProbe("net-port-probe"));
        Assert.Equal(ProbeMode.Live, SecurityModeCatalog.ModeOfProbe("NET-PORT-PROBE"));
        Assert.Equal(ProbeMode.Static, SecurityModeCatalog.ModeOfProbe("future-probe"));
    }
}
