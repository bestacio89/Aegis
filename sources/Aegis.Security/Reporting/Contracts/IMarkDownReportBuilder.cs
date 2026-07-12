using Aegis.Shared.Security.Models;

namespace Aegis.Security.Reporting.Contracts;

public interface IMarkdownReportBuilder
{
    string Build(AegisSecurityReport report);
}