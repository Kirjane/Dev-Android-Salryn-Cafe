using System.Security.Cryptography;
using System.Text;

namespace Salryn.Infrastructure.Licensing;

/// <summary>
/// Builds a stable hashed machine code for offline license binding.
/// Raw identifiers (MachineGuid, installation id) are never returned to the UI.
/// </summary>
public static class LocalMachineFingerprint
{
    public const string MachineCodePrefix = "SALRYN-MC-";
    public const string InstallationIdFileName = "installation-id.txt";

    /// <summary>
    /// Returns the hashed machine code used as MachineBinding (e.g. SALRYN-MC-ABCD-EFGH-IJKL-MNOP).
    /// </summary>
    public static string Current()
    {
        string material = string.Join(
            "|",
            ReadWindowsMachineGuid() ?? "machine-guid-unavailable",
            GetOrCreateInstallationId(),
            Environment.MachineName ?? "unknown-machine",
            Environment.OSVersion.Platform.ToString());

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return FormatMachineCode(hash);
    }

    /// <summary>Formats the first 8 bytes of a hash as a grouped display machine code.</summary>
    public static string FormatMachineCode(ReadOnlySpan<byte> hash)
    {
        if (hash.Length < 8)
        {
            throw new ArgumentException("Hash material must be at least 8 bytes.", nameof(hash));
        }

        string hex = Convert.ToHexString(hash[..8]);
        return MachineCodePrefix +
            hex[..4] + "-" +
            hex[4..8] + "-" +
            hex[8..12] + "-" +
            hex[12..16];
    }

    public static string GetDefaultLicensingRoot()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        return Path.Combine(localAppData, "Salryn", "Licensing");
    }

    public static string GetOrCreateInstallationId(string? licensingRoot = null)
    {
        string root = string.IsNullOrWhiteSpace(licensingRoot)
            ? GetDefaultLicensingRoot()
            : Path.GetFullPath(licensingRoot.Trim());
        Directory.CreateDirectory(root);

        string path = Path.Combine(root, InstallationIdFileName);
        if (File.Exists(path))
        {
            string existing = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(existing) &&
                existing.Length <= 128 &&
                !existing.Contains('\n') &&
                !existing.Contains('\r'))
            {
                return existing;
            }
        }

        string created = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, created, Encoding.UTF8);
        return created;
    }

    private static string? ReadWindowsMachineGuid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Cryptography");
            object? value = key?.GetValue("MachineGuid");
            string? text = value?.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
