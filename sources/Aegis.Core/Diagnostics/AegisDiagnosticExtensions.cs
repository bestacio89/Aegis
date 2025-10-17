using Aegis.Shared.Diagnostics;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aegis.Core.Diagnostics
{
    public static class AegisDiagnosticsExtensions
    {
        public static void UseSerilogBridge()
        {
            AegisDiagnostics.OnEvent += evt =>
            {
                switch (evt.Level)
                {
                    case DiagnosticLevel.Trace: Log.Verbose("[{Source}] {Msg}", evt.Source, evt.Message); break;
                    case DiagnosticLevel.Info: Log.Information("[{Source}] {Msg}", evt.Source, evt.Message); break;
                    case DiagnosticLevel.Warning: Log.Warning("[{Source}] {Msg}", evt.Source, evt.Message); break;
                    case DiagnosticLevel.Error:
                    case DiagnosticLevel.Critical:
                        Log.Error(evt.Exception, "[{Source}] {Msg}", evt.Source, evt.Message);
                        break;
                }
            };
        }
    }

}
