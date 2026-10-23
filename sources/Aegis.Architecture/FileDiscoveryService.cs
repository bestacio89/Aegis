using Aegis.Architecture.Analysis.Detectors;

namespace Aegis.Architecture;

public static class FileDiscoveryService
{
    /// <summary>
    /// Returns governed files with the requested extensions. The same shared
    /// exclusion policy used by architecture discovery is applied to both
    /// directory traversal and individual files, preventing generated caches
    /// and vendored examples from re-entering through this secondary discovery path.
    /// </summary>
    public static IEnumerable<string> GetScopedFiles(
        string rootPath,
        string[] extensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(extensions);

        if (!Directory.Exists(rootPath))
            return [];

        var normalizedExtensions = extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.StartsWith('.', StringComparison.Ordinal)
                ? extension
                : $".{extension}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (normalizedExtensions.Count == 0)
            return [];

        var files = new List<string>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(Path.GetFullPath(rootPath));

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            if (FileExclusions.IsExcluded(currentDirectory))
                continue;

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(
                    currentDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in childDirectories)
            {
                if (FileExclusions.IsExcluded(directory))
                    continue;

                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
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

                pendingDirectories.Push(directory);
            }

            IEnumerable<string> currentFiles;
            try
            {
                currentFiles = Directory.EnumerateFiles(
                    currentDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            files.AddRange(currentFiles.Where(file =>
                normalizedExtensions.Contains(Path.GetExtension(file))
                && !FileExclusions.IsExcluded(file)));
        }

        return files;
    }
}
