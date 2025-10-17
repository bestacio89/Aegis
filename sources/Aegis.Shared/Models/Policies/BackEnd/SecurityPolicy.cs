using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Models.Policies.BackEnd
{
    /// <summary> Governs security scanning policies. </summary>
    public class SecurityPolicy
    {
        public bool ScanForHardcodedSecrets { get; set; } = true;
        public bool EnforceSafeCryptography { get; set; } = true;
        public bool DetectWeakSSLProtocols { get; set; } = true;
    }
}
