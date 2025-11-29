using Aegis;
using Aegis.Security;
using Aegis.Security.Kernel;
using Aegis.Security.Kernel.OS;
using Aegis.Security.Kernel.OS.Probes;
using Aegis.Security.Kernel.OS.Signals;
using System.Runtime.InteropServices;

namespace Aegis.Security.Kernel.OS.Probes;

public sealed class OsHardeningProbe
{
    public OsHardeningSecuritySignal Probe()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProbeWindows();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ProbeLinux();

        // Fallback for unsupported OS
        return new OsHardeningSecuritySignal
        {
            FirewallEnabled = false,
            UacEnabled = false,
            IsGuestAccountEnabled = false,
            SecureBootEnabled = false,
            Smb1Enabled = false,
        };
    }

    // ---------------------------
    // WINDOWS HARDENING CHECKS
    // ---------------------------
    private OsHardeningSecuritySignal ProbeWindows()
    {
        bool firewall = IsWindowsFirewallEnabled();
        bool uac = IsUacEnabled();
        bool guest = IsGuestAccountEnabled();
        bool smb1 = IsSmb1Enabled();
        bool secureBoot = IsSecureBootEnabled();

        return new OsHardeningSecuritySignal
        {
            FirewallEnabled = firewall,
            UacEnabled = uac,
            IsGuestAccountEnabled = guest,
            SecureBootEnabled = secureBoot,
            Smb1Enabled = smb1
        };
    }

    // Helper methods use registry or powershell-safe techniques
    private bool IsWindowsFirewallEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile");
            return (int?)key?.GetValue("EnableFirewall") == 1;
        }
        catch { return false; }
    }

    private bool IsUacEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            return (int?)key?.GetValue("EnableLUA") == 1;
        }
        catch { return false; }
    }

    private bool IsGuestAccountEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SAM\SAM\Domains\Account\Users\000001F5");
            return key != null; // this key exists when Guest is enabled
        }
        catch { return false; }
    }

    private bool IsSmb1Enabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters");
            return (int?)key?.GetValue("SMB1") == 1;
        }
        catch { return false; }
    }

    private bool IsSecureBootEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return (int?)key?.GetValue("UEFISecureBootEnabled") == 1;
        }
        catch { return false; }
    }

    // ---------------------------
    // LINUX HARDENING CHECKS
    // ---------------------------
    private OsHardeningSecuritySignal ProbeLinux()
    {
        bool firewallEnabled = Run("ufw status").Contains("active") ||
                               Run("systemctl is-active firewalld").Contains("active");

        bool selinux = Run("getenforce").Contains("Enforcing");
        bool appArmor = Run("aa-status").Contains("profiles");

        return new OsHardeningSecuritySignal
        {
            FirewallEnabled = firewallEnabled,
            UacEnabled = false,
            IsGuestAccountEnabled = false,
            SecureBootEnabled = false,
            Smb1Enabled = false,
            AdditionalChecks = new Dictionary<string, string>
            {
                ["SELinux"] = selinux ? "Enforcing" : "Disabled",
                ["AppArmor"] = appArmor ? "Active" : "Inactive"
            }
        };
    }

    private static string Run(string cmd)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{cmd}\"",
                RedirectStandardOutput = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            return process?.StandardOutput.ReadToEnd() ?? "";
        }
        catch { return ""; }
    }
}
