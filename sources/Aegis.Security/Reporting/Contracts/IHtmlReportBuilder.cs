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
    public interface IHtmlReportBuilder
    {
        string Build(AegisSecurityReport report);
    }
}
