using Aegis.Security.Kernel.OS.Signals;

namespace Aegis.Security.Kernel.OS;

public sealed class OsInfoProbe
{
    public OsInfoSecuritySignal Collect()
    {
        return new OsInfoSecuritySignal
        {
            OSVersion = Environment.OSVersion.ToString(),
            MachineName = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            Is64Bit = Environment.Is64BitOperatingSystem,
            FrameworkVersion = Environment.Version.ToString()
        };
    }
}
