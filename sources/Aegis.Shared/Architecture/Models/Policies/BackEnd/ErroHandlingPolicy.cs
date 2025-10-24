using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Architecture.Models.Policies.BackEnd
{
    /// <summary> Governs exception-handling rules. </summary>
    public class ErrorHandlingPolicy
    {
        public bool AllowEmptyCatch { get; set; } = false;
        public bool AllowGenericCatch { get; set; } = false;
        public bool RequireLoggingOrRethrow { get; set; } = true;
    }
}
