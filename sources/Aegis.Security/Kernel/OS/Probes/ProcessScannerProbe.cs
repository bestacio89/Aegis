using Aegis.Security.Kernel.OS.Signals;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Aegis.Security.Kernel.OS.Probes
{
    public sealed class ProcessScannerProbe
    {
        public IEnumerable<ProcessSecuritySignal> Scan()
        {
            foreach (var process in Process.GetProcesses())
            {
                yield return new ProcessSecuritySignal
                {
                    Pid = process.Id,
                    Name = process.ProcessName,
                    MemoryMb = process.WorkingSet64 / (1024 * 1024),
                    StartTimeUtc = TryGetStartTime(process)
                };
            }
        }

        private static DateTime? TryGetStartTime(Process p)
        {
            try { return p.StartTime.ToUniversalTime(); }
            catch { return null; }
        }
    }
}
