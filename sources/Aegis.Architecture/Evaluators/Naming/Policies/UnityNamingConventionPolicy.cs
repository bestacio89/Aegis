using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.Naming.Policies;

/// <summary>
/// Unity-oriented C# naming conventions. In addition to normal C# identifiers,
/// accepts the common serialized/private field prefix <c>m_</c> used by Unity
/// projects and samples.
/// </summary>
public sealed class UnityNamingConventionPolicy : INamingConventionPolicy
{
    private static readonly Regex PascalCase = new(@"^[A-Z][a-zA-Z0-9]*$");
    private static readonly Regex InterfaceName = new(@"^I[A-Z][a-zA-Z0-9]*$");
    private static readonly Regex VariableName = new(@"^(?:[a-z_][a-zA-Z0-9]*|m_[A-Z][a-zA-Z0-9]*)$");
    private static readonly Regex ConstantName = new(@"^(?:[A-Z][a-zA-Z0-9]*|[A-Z][A-Z0-9_]+)$");

    public bool IsValidTypeName(string name) => PascalCase.IsMatch(name);

    public bool IsValidInterfaceName(string name) => InterfaceName.IsMatch(name);

    public bool IsValidMethodName(string name) => PascalCase.IsMatch(name);

    public bool IsValidVariableName(string name) => VariableName.IsMatch(name);

    public bool IsValidConstantName(string name) => ConstantName.IsMatch(name);

    public bool IsValidFileName(string name) => PascalCase.IsMatch(name);
}
