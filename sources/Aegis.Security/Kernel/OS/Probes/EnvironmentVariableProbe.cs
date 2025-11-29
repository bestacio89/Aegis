using Aegis.Security.Kernel.OS.Signals;
using System.Collections;

namespace Aegis.Security.Kernel.OS;

public sealed class EnvironmentVariableProbe
{
    public IEnumerable<EnvironmentVariableSecuritySignal> Collect()
    {
        foreach (DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            yield return new EnvironmentVariableSecuritySignal
            {
                Key = kv.Key.ToString()!,
                Value = kv.Value?.ToString()
            };
        }
    }
}
