namespace Aegis.Shared.Security.Live;

/// <summary>A single host:port an assessor is authorized to probe. Hostnames or IP addresses only.</summary>
public sealed record LiveTarget(string Host, int Port = 443)
{
    public string Display => $"{Host}:{Port}";

    public override string ToString() => Display;
}