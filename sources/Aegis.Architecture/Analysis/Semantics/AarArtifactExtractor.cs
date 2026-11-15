using System.IO.Compression;
using System.Security.Cryptography;

namespace Aegis.Architecture.Analysis.Semantics;

/// <summary>
/// Extracts only compiler class archives from a locally resolved Android AAR.
/// Extraction is content-addressed, bounded, and never writes archive-controlled paths.
/// </summary>
public sealed class AarArtifactExtractor
{
    private const long MaximumExtractedBytes = 256L * 1024 * 1024;
    private const int MaximumNestedArchives = 256;
    private readonly string _cacheRoot;

    public AarArtifactExtractor(string? cacheRoot = null)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot
            ?? Path.Combine(Path.GetTempPath(), "Aegis", "JvmArtifacts"));
    }

    public IReadOnlyList<string> Extract(string aarPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aarPath);
        var fullPath = Path.GetFullPath(aarPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The Android archive does not exist.", fullPath);

        using var source = File.OpenRead(fullPath);
        var contentHash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        using var archive = ZipFile.OpenRead(fullPath);

        var selectedEntries = archive.Entries
            .Where(entry => IsCompilerArchive(entry.FullName))
            .OrderBy(entry => entry.FullName == "classes.jar" ? 0 : 1)
            .ThenBy(entry => entry.FullName, StringComparer.Ordinal)
            .ToArray();

        if (selectedEntries.Length > MaximumNestedArchives)
            throw new InvalidDataException("The Android archive contains too many nested compiler archives.");

        if (selectedEntries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count()
            != selectedEntries.Length)
            throw new InvalidDataException("The Android archive contains duplicate compiler archive entries.");

        long totalLength = 0;
        foreach (var entry in selectedEntries)
        {
            if (entry.Length < 0 || entry.Length > MaximumExtractedBytes - totalLength)
                throw new InvalidDataException("The Android archive exceeds the nested compiler archive size limit.");

            totalLength += entry.Length;
        }

        if (selectedEntries.Length == 0)
            return Array.Empty<string>();

        var destinationDirectory = Path.Combine(_cacheRoot, contentHash);
        Directory.CreateDirectory(destinationDirectory);
        var results = new List<string>(selectedEntries.Length);

        foreach (var entry in selectedEntries)
        {
            var fileName = entry.FullName == "classes.jar"
                ? "classes.jar"
                : $"{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(entry.FullName))).ToLowerInvariant()}-{Path.GetFileName(entry.FullName)}";
            var destination = Path.Combine(destinationDirectory, fileName);

            if (!File.Exists(destination) || new FileInfo(destination).Length != entry.Length)
                ExtractEntry(entry, destination);

            results.Add(destination);
        }

        return results;
    }

    private static bool IsCompilerArchive(string entryName)
    {
        if (entryName.Equals("classes.jar", StringComparison.Ordinal))
            return true;

        if (!entryName.StartsWith("libs/", StringComparison.Ordinal)
            || !entryName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            return false;

        var fileName = entryName["libs/".Length..];
        return fileName.Length > 4
            && !fileName.Contains('/')
            && !fileName.Contains('\\')
            && fileName is not "." and not "..";
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string destination)
    {
        var temporaryPath = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var input = entry.Open())
            using (var output = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                if (output.Length != entry.Length)
                    throw new InvalidDataException("A nested compiler archive was truncated during extraction.");
            }

            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
