using Franz.Common.DependencyInjection;

namespace Aegis.Sdk.Contracts;

/// <summary>
/// Defines the base lifecycle of an Aegis plugin.
/// </summary>
public interface IPlugin : IScopedDependency
{
 
    void Initialize();
    void Shutdown();
}
