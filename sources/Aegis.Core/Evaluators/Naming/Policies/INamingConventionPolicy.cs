namespace Aegis.Architecture.Evaluators.Naming.Policies;


public interface INamingConventionPolicy
{
    bool IsValidTypeName(string name);

    bool IsValidInterfaceName(string name);

    bool IsValidMethodName(string name);

    bool IsValidVariableName(string name);

    bool IsValidConstantName(string name);

    bool IsValidFileName(string name);
}