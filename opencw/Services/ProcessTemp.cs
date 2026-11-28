using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace OpenCommonwealth.Services;

public static class ProcessTemp
{
    private static readonly TimeSpan LegacyGrace = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<(string, string, bool), byte> Swept = new();

    public static string PathFor(string target, string suffix = ".tmp") => target + "." + System.Environment.ProcessId + suffix;

    public static void SweepDead(string target, string suffix = ".tmp", bool ignoreGraceForDeadOwner = false)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(target));
        if (dir == null || !Directory.Exists(dir)) return;
        var name = Path.GetFileName(target);
        var legacy = Path.Combine(dir, name + suffix);
        if (File.Exists(legacy)) SweepOne(legacy, 0, ignoreGraceForDeadOwner);
        foreach (var file in FilesIn(dir, name + ".*" + suffix))
        {
            var rest = Path.GetFileName(file)[name.Length..];
            if (rest.Length > suffix.Length + 1 && rest.EndsWith(suffix, StringComparison.Ordinal)
                && int.TryParse(rest[1..^suffix.Length], NumberStyles.None, CultureInfo.InvariantCulture, out var owner))
                SweepOne(file, owner, ignoreGraceForDeadOwner);
        }
    }

    public static void SweepDeadIn(string dir, string suffix = ".tmp", bool unparsedIsLegacy = false)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in FilesIn(dir, "*" + suffix)) SweepFile(file, suffix, unparsedIsLegacy);
    }

    public static void SweepDeadInOnce(string dir, string suffix = ".tmp", bool unparsedIsLegacy = false)
    {
        var key = (Path.GetFullPath(dir), suffix, unparsedIsLegacy);
        if (Swept.ContainsKey(key) || !Directory.Exists(dir)) return;
        SweepDeadIn(dir, suffix, unparsedIsLegacy);
        Swept.TryAdd(key, 0);
    }

    public static void MoveWithRetry(string from, string to, Action<string, string>? move = null)
    {
        move ??= (a, b) => File.Move(a, b, overwrite: true);
        for (int attempt = 0; ; attempt++)
        {
            try { move(from, to); return; }
            catch (Exception ex) when (attempt < 10 && IsTransient(ex, to, OperatingSystem.IsWindows())) { Thread.Sleep(20 * (attempt + 1)); }
        }
    }

    public static void WriteThenMove(string dest, Action<string> write)
    {
        var full = Path.GetFullPath(dest);
        var stem = Path.Combine(Path.GetDirectoryName(full)!, "." + Path.GetFileNameWithoutExtension(full));
        var suffix = ".tmp" + Path.GetExtension(full);
        SweepDead(stem, suffix, ignoreGraceForDeadOwner: true);
        var temp = PathFor(stem, suffix);
        try
        {
            write(temp);
            MoveWithRetry(temp, full);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static bool IsTransient(Exception ex, string to, bool windows) =>
        (ex is IOException and not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException) && !IsDiskFull(ex))
        || (ex is UnauthorizedAccessException && windows && !IsReadOnlyFile(to));

    private static bool IsDiskFull(Exception ex) => ex.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027);

    private static bool IsReadOnlyFile(string path) => File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;

    internal static string[] FilesIn(string dir, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
    {
        try
        {
            return Directory.GetFiles(dir, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = option == SearchOption.AllDirectories,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                MatchType = MatchType.Win32,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    public static void SweepFile(string file, string suffix, bool unparsedIsLegacy = false)
    {
        var name = Path.GetFileName(file);
        if (!name.EndsWith(suffix, StringComparison.Ordinal)) return;
        var stem = name[..^suffix.Length];
        var dot = stem.LastIndexOf('.');
        if (dot == 0) SweepOne(file, 0);
        else if (dot > 0 && int.TryParse(stem[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) SweepOne(file, pid);
        else if (unparsedIsLegacy) SweepOne(file, 0);
    }

    private static void SweepOne(string file, int owner, bool ignoreGraceForDeadOwner = false)
    {
        if (owner == System.Environment.ProcessId) return;
        if (owner != 0 && OwnsFile(owner, file)) return;
        if (!ignoreGraceForDeadOwner || owner == 0)
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < LegacyGrace) return;
        }
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static bool OwnsFile(int pid, string file)
    {
        try
        {
            var writtenUtc = File.GetLastWriteTimeUtc(file);
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.StartTime.ToUniversalTime() <= writtenUtc.AddSeconds(2);
        }
        catch (Win32Exception) { return true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException) { return false; }
    }
}
