using Aegis.Shared.Security.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.Security.Kernel.OS.Signals
{
    public sealed record EnvironmentVariableSecuritySignal : SecuritySignal
    {
        public string Key { get; init; } = default!;
        public string? Value { get; init; }
    }
}
