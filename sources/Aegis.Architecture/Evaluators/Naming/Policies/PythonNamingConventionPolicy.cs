using System.Text.RegularExpressions;
namespace Aegis.Architecture.Evaluators.Naming.Policies;

public sealed class PythonNamingConventionPolicy : INamingConventionPolicy
{
    public bool IsValidTypeName(string name)
        => Regex.IsMatch(name, @"^[A-Z][a-zA-Z0-9]+$");


    public bool IsValidInterfaceName(string name)
        => true;


    public bool IsValidMethodName(string name)
        => Regex.IsMatch(name, @"^[a-z_][a-z0-9_]*$");


    public bool IsValidVariableName(string name)
        => Regex.IsMatch(name, @"^[a-z_][a-z0-9_]*$");


    public bool IsValidConstantName(string name)
        => Regex.IsMatch(name, @"^[A-Z][A-Z0-9_]*$");


    public bool IsValidFileName(string name)
        => Regex.IsMatch(name, @"^[a-z_][a-z0-9_]*$");
}