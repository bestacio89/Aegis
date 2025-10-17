namespace Aegis.Sdk.Manifests;

/// <summary>
/// Declares plugin metadata directly on the assembly or root plugin class.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = false)]
public sealed class ManifestAttribute : Attribute
{
    public string Name { get; }
    public string Version { get; }
    public string Author { get; }
    public string Description { get; }
    public string SupportedLanguages { get; }

    public ManifestAttribute(
        string name,
        string version = "1.0.0",
        string author = "Unknown",
        string description = "",
        string supportedLanguages = "All")
    {
        Name = name;
        Version = version;
        Author = author;
        Description = description;
        SupportedLanguages = supportedLanguages;
    }
}
