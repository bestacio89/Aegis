
using System.Text.RegularExpressions;

namespace Aegis.Architecture.Evaluators.Naming.Policies;

public sealed class CSharpNamingConventionPolicy : INamingConventionPolicy
{
    public bool IsValidTypeName(string name)
        => Regex.IsMatch(name, @"^[A-Z][a-zA-Z0-9]+$");


    public bool IsValidInterfaceName(string name)
        => name.StartsWith("I") &&
           Regex.IsMatch(name, @"^I[A-Z][a-zA-Z0-9]+$");


    public bool IsValidMethodName(string name)
        => Regex.IsMatch(name, @"^[A-Z][a-zA-Z0-9]+$");


    public bool IsValidVariableName(string name)
        => Regex.IsMatch(name, @"^[a-z_][a-zA-Z0-9]*$");


    public bool IsValidConstantName(string name)
        => Regex.IsMatch(name, @"^[A-Z][A-Z0-9_]*$");


    public bool IsValidFileName(string name)
        => IsValidTypeName(name);
} 