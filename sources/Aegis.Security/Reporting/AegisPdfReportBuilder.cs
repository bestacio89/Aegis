using Aegis.Security.Reporting.Contracts;
using Aegis.Shared.Security.Models;
using System.Text;

namespace Aegis.Security.Reporting;

/// <summary>
/// Basic PDF builder that currently returns UTF8 HTML bytes.
/// Later this can be wired to a real PDF engine (QuestPDF, PdfSharp, etc.).
/// </summary>
public sealed class AegisPdfReportBuilder : IPdfReportBuilder
{
    private readonly IHtmlReportBuilder _htmlBuilder;

    public AegisPdfReportBuilder(IHtmlReportBuilder htmlBuilder)
    {
        _htmlBuilder = htmlBuilder;
    }

    public byte[] Build(AegisSecurityReport report, SecurityComplianceResult compliance)
    {
        // v0: produce HTML bytes (good enough for WPF / browser preview / placeholder PDF).
        // v1: replace this with real PDF rendering.
        var html = _htmlBuilder.Build(report, compliance);
        return Encoding.UTF8.GetBytes(html);
    }
}
