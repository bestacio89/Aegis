using Aegis.Security.Reporting.Contracts;
using Aegis.Shared.Security.Models;
using System.Net;
using System.Text;

namespace Aegis.Security.Reporting;

public sealed class HtmlReportBuilder : IHtmlReportBuilder
{
    private readonly IMarkdownReportBuilder _markdown;

    public HtmlReportBuilder(IMarkdownReportBuilder markdown)
    {
        _markdown = markdown;
    }

    public string Build(AegisSecurityReport report, SecurityComplianceResult compliance)
    {
        // Easiest: embed markdown in <pre> until you wire a markdown→HTML converter.
        var md = _markdown.Build(report, compliance);
        var encoded = WebUtility.HtmlEncode(md);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine($"  <title>Aegis Security Report — {WebUtility.HtmlEncode(report.ProjectName)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: system-ui, -apple-system, Segoe UI, sans-serif; padding: 1.5rem; }");
        sb.AppendLine("    pre { white-space: pre-wrap; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <pre>");
        sb.AppendLine(encoded);
        sb.AppendLine("  </pre>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}
