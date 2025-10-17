using System;
using System.IO;
using System.Linq;

namespace Aegis.Shared.Utilities
{
    /// <summary>
    /// Provides helper utilities for filtering and analyzing filesystem paths.
    /// Commonly used by evaluators and analysis modules to skip build artifacts,
    /// temporary folders, and irrelevant infrastructure directories.
    /// </summary>
    public static class PathUtils
    {
        // 🧱 Common folders and artifacts that should be ignored across all languages.
        private static readonly string[] _excludedDirs =
        [
            ".git",
            "bin",
            "obj",
            "node_modules",
            "dist",
            "build",
            ".vs",
            ".idea",
            "__pycache__",
            ".venv",
            ".pytest_cache"
        ];

        /// <summary>
        /// Determines whether the provided path is located in an excluded directory
        /// (e.g., build artifacts, version control metadata, IDE folders, etc.).
        /// </summary>
        /// <param name="path">Full or relative directory path.</param>
        /// <returns><c>true</c> if the path should be excluded; otherwise, <c>false</c>.</returns>
        public static bool IsExcludedDir(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return _excludedDirs.Any(ex =>
                path.Contains(ex, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Filters a sequence of file paths by excluding known irrelevant directories.
        /// Useful for reducing noise during recursive searches.
        /// </summary>
        /// <param name="paths">Enumerable of file paths.</param>
        /// <returns>Enumerable containing only non-excluded file paths.</returns>
        public static IEnumerable<string> ExcludeNoise(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (!IsExcludedDir(Path.GetDirectoryName(path)))
                    yield return path;
            }
        }

        /// <summary>
        /// Determines if a directory is likely a source folder (e.g. contains code files).
        /// </summary>
        public static bool LooksLikeSourceDir(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var lower = path.ToLowerInvariant();
            return lower.Contains("src") || lower.Contains("source") || lower.Contains("app") || lower.Contains("lib");
        }
    }
}
