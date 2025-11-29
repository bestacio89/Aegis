using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace Aegis.Security.Kernel.FileSystem;

public sealed class FileSystemScanner
{
    private static readonly string[] SensitiveExtensions =
    {
        ".env", ".pem", ".key", ".pfx", ".csr", ".crt", ".config", ".json"
    };

    private static readonly Regex SecretsPattern = new(
        @"(password\s*=\s*.+|secret\s*=\s*.+|token\s*=\s*.+|apikey|jwt|bearer\s+[a-zA-Z0-9\-._~+/]+=*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public FileSystemScanResult Scan(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return new FileSystemScanResult(filePath, false, false, false, false, false, 0);

            var info = new FileInfo(filePath);
            var content = File.ReadAllText(filePath);
            var perms = GetPermissions(filePath);

            bool isSensitive = SensitiveExtensions.Contains(info.Extension.ToLowerInvariant());
            bool containsSecrets = SecretsPattern.IsMatch(content);

            return new FileSystemScanResult(
                Path: filePath,
                Exists: true,
                IsSensitive: isSensitive,
                IsWorldReadable: perms.IsWorldReadable,
                IsWorldWritable: perms.IsWorldWritable,
                ContainsSecrets: containsSecrets,
                SizeBytes: info.Length
            );
        }
        catch (Exception ex)
        {
            return new FileSystemScanResult(
                filePath,
                false,
                false,
                false,
                false,
                false,
                0,
                Error: ex.Message
            );
        }
    }

    private (bool IsWorldReadable, bool IsWorldWritable) GetPermissions(string filePath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetWindowsPermissions(filePath);

            // Linux / macOS
            return GetUnixPermissions(filePath);
        }
        catch
        {
            return (false, false);
        }
    }
    private (bool IsWorldReadable, bool IsWorldWritable) GetWindowsPermissions(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var access = fileInfo.GetAccessControl(); // ✔ Valid in .NET 10
            var rules = access.GetAccessRules(true, true, typeof(NTAccount));

            bool worldReadable = false;
            bool worldWritable = false;

            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.IdentityReference.Value.EndsWith("Everyone", StringComparison.OrdinalIgnoreCase))
                {
                    if (rule.FileSystemRights.HasFlag(FileSystemRights.Read))
                        worldReadable = true;

                    if (rule.FileSystemRights.HasFlag(FileSystemRights.Write))
                        worldWritable = true;
                }
            }

            return (worldReadable, worldWritable);
        }
        catch
        {
            return (false, false);
        }
    }


    private (bool IsWorldReadable, bool IsWorldWritable) GetUnixPermissions(string filePath)
    {
        try
        {
            UnixFileMode mode = File.GetUnixFileMode(filePath);

            bool worldReadable = mode.HasFlag(UnixFileMode.OtherRead);
            bool worldWritable = mode.HasFlag(UnixFileMode.OtherWrite);

            return (worldReadable, worldWritable);
        }
        catch
        {
            return (false, false);
        }
    }


}
