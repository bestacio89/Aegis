using System.Text.Json;

using Aegis.Architecture.Analysis.Detectors;
using Aegis.Shared.Architecture.Models;

namespace Aegis.Architecture.Analysis;

public static class ProjectArchitectureContextDetector
{
    public static ProjectArchitectureContext Detect(string rootPath)
    {
        var ctx = new ProjectArchitectureContext
        {
            ProjectName =
                Path.GetFileName(
                    rootPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)),

            RootPath = rootPath,

            DetectorVersion = "1.5.0",

            DetectionStrategy = "Hybrid"
        };


        var files = EnumerateGovernedFiles(rootPath, ctx.DiscoveryDiagnostics, ctx.ExcludedEntriesByReason);



        ctx.FileCount = files.Count;



        // ===============================================================
        // Technology Detection
        // ===============================================================

        LanguageAndBuildDetector.Analyze(
            ctx,
            files);


        FrameworkDetector.Analyze(
            ctx,
            files);


        DependencyDetector.Analyze(
            ctx,
            files);



        // ===============================================================
        // Structural Detection
        // ===============================================================

        ModuleDetector.Analyze(
            ctx,
            files);


        LayerDetector.Analyze(
            ctx,
            files);


        DomainDetector.Analyze(
            ctx);



        // ===============================================================
        // Architecture Detection
        // ===============================================================

        ArchitectureDetector.Analyze(
            ctx);


        PatternDetector.Analyze(
            ctx);


        BoundaryDetector.Analyze(
            ctx);



        // ===============================================================
        // Dependency Mapping
        // ===============================================================

        DependencyDetector.Analyze(
            ctx,
            files);



        // ===============================================================
        // Runtime Detection
        // ===============================================================

        DeployabilityDetector.Analyze(
            ctx,
            files);



        // ===============================================================
        // Project Identity
        // ===============================================================

        NatureDetector.Analyze(
            ctx,
            files);


        EntryPointDetector.Analyze(
            ctx,
            files);



        // ===============================================================
        // Final Context Enrichment
        // ===============================================================

        ArchitectureConfidenceEvaluator.Evaluate(
            ctx);



        PopulateContextMetadata(
            ctx,
            files);



        return ctx;
    }



    private static List<string> EnumerateGovernedFiles(
        string rootPath,
        ICollection<AnalysisDiagnostic> diagnostics,
        IDictionary<string, int> excludedEntriesByReason)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(rootPath));

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var directoryExclusionReason = FileExclusions.GetExclusionReason(directory);
            if (directoryExclusionReason is not null)
            {
                RecordExcludedEntry(excludedEntriesByReason, directoryExclusionReason);
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var exclusionReason = FileExclusions.GetExclusionReason(file);
                    if (exclusionReason is null)
                        files.Add(file);
                    else
                        RecordExcludedEntry(excludedEntriesByReason, exclusionReason);
                }

                foreach (var child in Directory.EnumerateDirectories(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    var exclusionReason = FileExclusions.GetExclusionReason(child);
                    if (exclusionReason is not null)
                    {
                        RecordExcludedEntry(excludedEntriesByReason, exclusionReason);
                        continue;
                    }

                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch (IOException exception)
                    {
                        diagnostics.Add(new AnalysisDiagnostic
                        {
                            Code = "DirectoryMetadataUnavailable",
                            Component = nameof(ProjectArchitectureContextDetector),
                            Message = $"Unable to inspect directory metadata: {exception.Message}",
                            Target = child,
                            ExceptionType = exception.GetType().Name,
                            AffectsCoverage = true
                        });
                        continue;
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        diagnostics.Add(new AnalysisDiagnostic
                        {
                            Code = "DirectoryMetadataAccessDenied",
                            Component = nameof(ProjectArchitectureContextDetector),
                            Message = "Access to directory metadata was denied; the path was skipped.",
                            Target = child,
                            ExceptionType = exception.GetType().Name,
                            AffectsCoverage = true
                        });
                        continue;
                    }

                    pending.Push(child);
                }
            }
            catch (IOException exception)
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "DirectoryDiscoveryFailed",
                    Component = nameof(ProjectArchitectureContextDetector),
                    Message = $"Unable to enumerate entries in directory: {exception.Message}",
                    Target = directory,
                    ExceptionType = exception.GetType().Name,
                    AffectsCoverage = true
                });
                // Continue scanning readable siblings rather than aborting the repository scan.
            }
            catch (UnauthorizedAccessException exception)
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Code = "DirectoryAccessDenied",
                    Component = nameof(ProjectArchitectureContextDetector),
                    Message = "Access to a directory was denied; files under that path were not assessed.",
                    Target = directory,
                    ExceptionType = exception.GetType().Name,
                    AffectsCoverage = true
                });
                // An inaccessible subtree is excluded from evidence, not treated as clean.
            }
        }

        return files;
    }

    private static void RecordExcludedEntry(
        IDictionary<string, int> excludedEntriesByReason,
        string reason)
    {
        excludedEntriesByReason[reason] =
            excludedEntriesByReason.GetValueOrDefault(reason) + 1;
    }

    private static void PopulateContextMetadata(
        ProjectArchitectureContext ctx,
        List<string> files)
    {
        ctx.MetadataMap["ProjectName"] =
            ctx.ProjectName;


        ctx.MetadataMap["FileCount"] =
            ctx.FileCount.ToString();

        ctx.MetadataMap["ExcludedPathEntryCount"] =
            ctx.ExcludedEntriesByReason.Values.Sum().ToString();

        ctx.MetadataMap["ExcludedEntriesByReason"] =
            JsonSerializer.Serialize(ctx.ExcludedEntriesByReason);


        ctx.MetadataMap["DetectedDependencies"] =
            ctx.DetectedDependencies.Count.ToString();


        ctx.MetadataMap["DetectedLayers"] =
            ctx.Layers.Count.ToString();


        ctx.MetadataMap["DetectedModules"] =
            ctx.Modules.Count.ToString();


        ctx.MetadataMap["LastModified"] =
            files
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.UtcNow)
                .Max()
                .ToString("O");
    }
}