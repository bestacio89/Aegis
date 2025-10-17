using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Models.Policies.Architecture
{
    /// <summary> Governs REST / API consistency. </summary>
    public class ApiConsistencyPolicy
    {
        public bool EnforceLowercaseRoutes { get; set; } = true;
        public bool EnforceNoTrailingSlash { get; set; } = true;
        public bool CheckPluralization { get; set; } = true;
    }
}
