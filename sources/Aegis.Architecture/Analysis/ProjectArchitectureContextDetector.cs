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


        var files = EnumerateGovernedFiles(rootPath);



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



    private static List<string> EnumerateGovernedFiles(string rootPath)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(rootPath));

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (FileExclusions.IsExcluded(directory))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    if (!FileExclusions.IsExcluded(file))
                        files.Add(file);
                }

                foreach (var child in Directory.EnumerateDirectories(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    if (FileExclusions.IsExcluded(child))
                        continue;

                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    pending.Push(child);
                }
            }
            catch (IOException)
            {
                // Continue scanning readable siblings rather than aborting the repository scan.
            }
            catch (UnauthorizedAccessException)
            {
                // An inaccessible subtree is excluded from evidence, not treated as clean.
            }
        }

        return files;
    }

    private static void PopulateContextMetadata(
        ProjectArchitectureContext ctx,
        List<string> files)
    {
        ctx.MetadataMap["ProjectName"] =
            ctx.ProjectName;


        ctx.MetadataMap["FileCount"] =
            ctx.FileCount.ToString();


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