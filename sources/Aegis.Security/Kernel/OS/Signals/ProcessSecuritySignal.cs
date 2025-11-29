using Aegis.Shared.Security.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.Security.Kernel.OS.Signals
{
    public sealed record ProcessSecuritySignal : SecuritySignal
    {
        public int Pid { get; init; }
        public string Name { get; init; } = default!;
        public long MemoryMb { get; init; }
        public DateTime? StartTimeUtc { get; init; }
    }
}
