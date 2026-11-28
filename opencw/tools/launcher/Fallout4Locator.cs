using System.Runtime.Versioning;
using System.Security;
#pragma warning disable CA1416
using Microsoft.Win32;
#pragma warning restore CA1416

namespace OpenCommonwealth.Launcher;

internal static class Fallout4Locator
{
    internal static bool IsValidInstall(string dir) =>
        File.Exists(Path.Combine(dir, "Fallout4.exe")) && File.Exists(Path.Combine(dir, "Data", "Fallout4.esm"));

    public static string? FindFallout4()
    {
        foreach (string dir in CandidateGameDirs().Prepend(LauncherSettings.Get("Fallout4Path") ?? ""))
        {
            if (dir.Length == 0) continue;
            try
            {
                if (IsValidInstall(dir))
                    return dir;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
            }
        }
        return null;
    }

    static IEnumerable<string> CandidateGameDirs()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var (hive, sub) in new[]
            {
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4"),
                (RegistryHive.LocalMachine, @"SOFTWARE\Bethesda Softworks\Fallout4"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 377160"),
            })
            {
                string? p = ReadReg(hive, sub, "Installed Path") ?? ReadReg(hive, sub, "InstallLocation");
                if (!string.IsNullOrWhiteSpace(p)) yield return p!;
            }
            string? steam = ReadReg(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")
                         ?? ReadReg(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            foreach (string lib in SteamLibraries(steam))
                yield return Path.Combine(lib, "steamapps", "common", "Fallout 4");
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var steamRoots = new[]
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            };
            foreach (var root in steamRoots)
                foreach (string lib in SteamLibraries(root))
                    yield return Path.Combine(lib, "steamapps", "common", "Fallout 4");
        }
    }

    static IEnumerable<string> SteamLibraries(string? steamPath)
    {
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath)) yield break;
        yield return steamPath;
        string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        string text;
        try { text = File.ReadAllText(vdf); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (string line in text.Split('\n'))
        {
            int i = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            int q1 = line.IndexOf('"', i + 6);
            int q2 = q1 >= 0 ? line.IndexOf('"', q1 + 1) : -1;
            if (q1 >= 0 && q2 > q1) yield return line[(q1 + 1)..q2].Replace("\\\\", "/");
        }
    }

    [SupportedOSPlatform("windows")]
    static string? ReadReg(RegistryHive hive, string subkey, string value)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subkey);
            return key?.GetValue(value) as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ObjectDisposedException)
        {
            return null;
        }
    }
}
