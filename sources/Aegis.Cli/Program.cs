using Aegis.Cli.Commands;
using Aegis.Cli.Services;
using Franz.Common.Logging.Tracing;
using System.CommandLine;

// 🧠 Aegis CLI — Deterministic Architecture Audit Tool
TraceHelper.LogConsole();

var root = new RootCommand("🧠 Aegis CLI — Deterministic Architecture Audit Tool")
{
    TreatUnmatchedTokensAsErrors = true
};

// =========================================================
// 🧩 Define the `analyze` command
// =========================================================
var analyzeCmd = new Command("analyze", "Run a full Aegis analysis on a project")
{
    new Argument<string>(
        "path",
        description: "Project root path to analyze",
        getDefaultValue: () => Directory.GetCurrentDirectory()),

    new Option<string?>(
        "--policy",
        description: "Optional policy JSON path (defaults to aegis.policy.json)"),

    new Option<bool>(
        "--json",
        description: "Export results as JSON report (default: true)")
};

// Attach handler (async delegate)
analyzeCmd.SetHandler(
    async (string path, string? policy, bool json) =>
    {
        await AnalyzeCommand.ExecuteAsync(path, policy, json);
    },
    (System.CommandLine.Binding.IValueDescriptor<string>)analyzeCmd.Arguments[0],
    (System.CommandLine.Binding.IValueDescriptor<string>)analyzeCmd.Options[0],
    (System.CommandLine.Binding.IValueDescriptor<bool>)analyzeCmd.Options[1]);

root.AddCommand(analyzeCmd);

// =========================================================
// 🚀 Run the CLI
// =========================================================
return await root.InvokeAsync(args);
