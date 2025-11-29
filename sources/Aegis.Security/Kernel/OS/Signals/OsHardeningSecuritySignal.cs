using Aegis.Shared.Security.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.Security.Kernel.OS.Signals
{
    public sealed record OsHardeningSecuritySignal : SecuritySignal
    {
        public bool FirewallEnabled { get; init; }
        public bool IsGuestAccountEnabled { get; init; }
        public bool UacEnabled { get; init; }
        public bool SecureBootEnabled { get; init; }

        public bool Smb1Enabled { get; init; }

        public IReadOnlyDictionary<string, string> AdditionalChecks { get; init; }
            = new Dictionary<string, string>();
    }
}
