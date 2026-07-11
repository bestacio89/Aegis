using System;
using System.Collections.Generic;
using System.Text;

namespace Aegis.Core
{
    public static class FileDiscoveryService
    {
        private static readonly string[] ExcludedFolders = { ".git", ".vs", "bin", "obj", "node_modules", ".nuget" };

        public static IEnumerable<string> GetScopedFiles(string rootPath, string[] extensions)
        {
            // Enumerate directory tree manually to allow for 'pruning' branches
            return Directory.EnumerateFileSystemEntries(rootPath, "*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    // 1. Ensure it's a file, not a folder
                    if (!File.Exists(path)) return false;

                    // 2. Ensure it's not inside an excluded folder (The "Sniper" Pruning)
                    if (ExcludedFolders.Any(ex => path.Contains(Path.DirectorySeparatorChar + ex + Path.DirectorySeparatorChar)))
                        return false;

                    // 3. Ensure it matches requested extensions
                    return extensions.Contains(Path.GetExtension(path).ToLower());
                });
        }
    }
}
