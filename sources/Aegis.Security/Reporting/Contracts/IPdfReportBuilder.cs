using Aegis;
using Aegis.Security;
using Aegis.Security.Reporting;
using Aegis.Security.Reporting.Contracts;
using Aegis.Shared.Security.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.Security.Reporting.Contracts
{
    /// <summary>
    /// Builds a binary PDF payload for the given report + compliance result.
    /// The implementation is intentionally library-agnostic; plug QuestPDF/PdfSharp later.
    /// </summary>
    public interface IPdfReportBuilder
    {
        byte[] Build(AegisSecurityReport report);
    }
}
