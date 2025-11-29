using Aegis.Shared.Security.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.Security.Kernel.OS.Signals
{
    public sealed record OsInfoSecuritySignal : SecuritySignal
    {
        public string OSVersion { get; init; } = default!;
        public string MachineName { get; init; } = default!;
        public bool Is64Bit { get; init; }
        public int ProcessorCount { get; init; }
        public string FrameworkVersion { get; init; } = default!;
    }
}
