namespace Aegis.Sdk.Exceptions;

/// <summary>
/// Represents errors that occur when loading or initializing plugins.
/// </summary>
public class PluginLoadException : Exception
{
    public PluginLoadException(string message) : base(message) { }
    public PluginLoadException(string message, Exception inner) : base(message, inner) { }
}
