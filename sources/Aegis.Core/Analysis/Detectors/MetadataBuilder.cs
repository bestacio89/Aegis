using Aegis.Shared.Architecture.Models;
using Aegis.Shared.Models;

namespace Aegis.Core.Analysis.Detectors;

internal static class MetadataBuilder
{
    public static void Populate(ProjectArchitectureContext ctx, List<string> files)
    {
        var now = DateTime.UtcNow;
        var name = Path.GetFileName(ctx.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var version = ctx.TargetRuntime ?? ctx.DetectedFrameworks.FirstOrDefault() ?? "N/A";
        var lastModified = files.Select(f => File.GetLastWriteTimeUtc(f)).DefaultIfEmpty(now).Max();

        ctx.Metadata = new ProjectArchitectureMetadata(
            Name: name,
            Path: ctx.RootPath,
            Language: ctx.Language,
            Framework: ctx.Framework ?? "Unknown",
            Version: version,
            LastModified: lastModified
        );
    }
}
