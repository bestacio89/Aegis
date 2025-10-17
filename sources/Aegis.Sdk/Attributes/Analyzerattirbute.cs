namespace Aegis.Sdk.Attributes;

/// <summary>
/// Marks a class as an analyzer or scanner plugin for discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AnalyzerAttribute : Attribute
{
    public string Name { get; }
    public string Language { get; }

    public AnalyzerAttribute(string name, string language)
    {
        Name = name;
        Language = language;
    }
}
