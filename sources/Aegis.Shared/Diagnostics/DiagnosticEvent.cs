using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Shared.Diagnostics
{
    public record DiagnosticEvent(
     string Source,
     DiagnosticLevel Level,
     string Message,
     DateTime Timestamp,
     Exception? Exception = null
 );
}
