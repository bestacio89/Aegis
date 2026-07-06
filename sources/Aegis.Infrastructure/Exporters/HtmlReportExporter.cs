using System.Text;
using Aegis.Shared.Architecture.Enums;
using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Contracts;
using Microsoft.Extensions.Logging;

namespace Aegis.Infrastructure.Exporters;

/// <summary>
/// 🌐 Executive HTML Report Exporter
/// Generates a standalone browser-readable architecture report.
/// </summary>
public sealed class HtmlReportExporter : IReportExporter
{
    public string Format => "html";

    private readonly ILogger<HtmlReportExporter> _logger;

    public HtmlReportExporter(
        ILogger<HtmlReportExporter> logger)
    {
        _logger = logger;
    }

    public async Task ExportAsync(
        AegisArchitectureReport report,
        ProjectArchitectureContext context,
        string outputPath,
        ArchitectureReportDetailLevel detailLevel = ArchitectureReportDetailLevel.FullForensic,
        CancellationToken token = default)
    {
        try
        {
            var html = new StringBuilder();

            html.AppendLine("""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Aegis Architecture Report</title>

<style>

body {
    font-family: Arial, Helvetica, sans-serif;
    margin: 40px;
    background: #f7f8fa;
    color: #222;
}

h1 {
    color: #1f4e79;
}

h2 {
    border-bottom: 2px solid #ddd;
    padding-bottom: 5px;
    color: #1f4e79;
}

.card {
    background:white;
    padding:20px;
    margin-bottom:25px;
    border-radius:8px;
    box-shadow:0 2px 6px rgba(0,0,0,.1);
}

table {
    width:100%;
    border-collapse:collapse;
    margin-top:10px;
}

th {
    background:#1f4e79;
    color:white;
    padding:8px;
}

td {
    border:1px solid #ddd;
    padding:8px;
}

.good {
    color:green;
    font-weight:bold;
}

.warning {
    color:#d68a00;
    font-weight:bold;
}

.bad {
    color:red;
    font-weight:bold;
}

.violation {
    margin-bottom:15px;
    padding:15px;
    background:#fff4f4;
    border-left:5px solid red;
}

.footer {
    margin-top:40px;
    color:#777;
    font-size:12px;
}

</style>

</head>

<body>
""");

            html.AppendLine(
                $"<h1>🧠 Aegis Executive Architecture Report</h1>");

            html.AppendLine(
                $"<p>Generated: {DateTime.UtcNow:u}</p>");

            // ======================================================
            // Project Information
            // ======================================================

            html.AppendLine("""
<section class="card">
<h2>📁 Project Information</h2>

<table>
<tr><th>Property</th><th>Value</th></tr>
""");

            AddRow(html, "Project", report.ProjectName);
            AddRow(html, "Path", report.ProjectPath);
            AddRow(html, "Language", context.Language);
            AddRow(html, "Framework", context.Framework);
            AddRow(html, "Architecture",
                context.ArchitectureStyle ?? "Unknown");
            AddRow(html, "Layer",
                context.Layer ?? "Unclassified");
            AddRow(html, "Build System",
                context.BuildSystem);
            AddRow(html, "Confidence",
                $"{context.Confidence:P0}");

            html.AppendLine("""
</table>
</section>
""");


            // ======================================================
            // Health
            // ======================================================

            html.AppendLine("""
<section class="card">
<h2>📊 Health Summary</h2>

<table>
<tr>
<th>Metric</th>
<th>Value</th>
</tr>
""");

            AddRow(html,
                "Files Scanned",
                report.TotalFilesScanned.ToString());

            AddRow(html,
                "Violations",
                report.TotalViolations.ToString());

            AddRow(html,
                "Health Index",
                $"{report.Metrics.ProjectHealthIndex:0.00}%");

            html.AppendLine("""
</table>
</section>
""");


            // ======================================================
            // Compliance
            // ======================================================

            html.AppendLine("""
<section class="card">
<h2>🧩 Compliance by Category</h2>

<table>
<tr>
<th>Category</th>
<th>Score</th>
<th>Status</th>
</tr>
""");


            foreach (var score in report.ComplianceScores)
            {
                var css =
                    score.Value >= 80 ? "good" :
                    score.Value >= 50 ? "warning" :
                    "bad";

                var status =
                    score.Value >= 80 ? "✅ OK" :
                    score.Value >= 50 ? "⚠ Warning" :
                    "❌ Critical";

                html.AppendLine($"""
<tr>
<td>{score.Key}</td>
<td>{score.Value:0.00}%</td>
<td class="{css}">
{status}
</td>
</tr>
""");
            }

            html.AppendLine("""
</table>
</section>
""");


            // ======================================================
            // Violations
            // ======================================================

            html.AppendLine("""
<section class="card">
<h2>🔍 Top Violations</h2>
""");


            foreach (var violation in report.Results
                         .OrderByDescending(x => x.WeightedImpact)
                         .Take(10))
            {
                html.AppendLine($"""
<div class="violation">

<h3>{Encode(violation.RuleName)}</h3>

<p>
<b>Severity:</b> {violation.Severity}<br/>
<b>Category:</b> {violation.Category}<br/>
<b>Target:</b> {Encode(violation.Target)}<br/>
<b>Impact:</b> {violation.WeightedImpact:0.00}
</p>

<p>
{Encode(violation.Message)}
</p>

</div>
""");
            }

            html.AppendLine("""
</section>
""");


            // ======================================================
            // Layer Analysis
            // ======================================================

            html.AppendLine("""
<section class="card">
<h2>🏗 Layer Analysis</h2>
""");


            foreach (var layer in report.Results
                     .GroupBy(x => x.Domain))
            {
                html.AppendLine($"""
<h3>{layer.Key}</h3>

<table>

<tr>
<th>Rule</th>
<th>Severity</th>
<th>Message</th>
</tr>
""");

                foreach (var result in layer.Take(10))
                {
                    html.AppendLine($"""
<tr>
<td>{Encode(result.RuleName)}</td>
<td>{result.Severity}</td>
<td>{Encode(result.Message)}</td>
</tr>
""");
                }

                html.AppendLine("</table>");
            }

            html.AppendLine("""
</section>
""");


            html.AppendLine($"""
<div class="footer">
Generated by Aegis Architecture Analyzer v1.0
</div>

</body>
</html>
""");


            await File.WriteAllTextAsync(
                outputPath,
                html.ToString(),
                Encoding.UTF8,
                token);


            _logger.LogInformation(
                "🌐 HTML report exported → {Path}",
                outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "❌ Failed generating HTML report → {Path}",
                outputPath);

            throw;
        }
    }


    private static void AddRow(
        StringBuilder builder,
        string name,
        object? value)
    {
        builder.AppendLine($"""
<tr>
<td>{Encode(name)}</td>
<td>{Encode(value?.ToString())}</td>
</tr>
""");
    }


    private static string Encode(string? value)
    {
        return System.Net.WebUtility.HtmlEncode(
            value ?? string.Empty);
    }
}