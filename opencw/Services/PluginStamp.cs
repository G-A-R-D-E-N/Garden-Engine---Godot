using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OpenCommonwealth.Services;

public static class PluginStamp
{
    public static string Line((string Name, long Size, long LastWriteUtcTicks) plugin) =>
        $"{plugin.Name.ToLowerInvariant()}|{plugin.Size}|{plugin.LastWriteUtcTicks}";

    public static void AppendLines(IncrementalHash hash, IEnumerable<(string Name, long Size, long LastWriteUtcTicks)> plugins)
    {
        foreach (var plugin in plugins)
            hash.AppendData(Encoding.UTF8.GetBytes(Line(plugin) + "\n"));
    }

    public static (long Size, long Ticks) Of(FileInfo info) => (info.Length, info.LastWriteTimeUtc.Ticks);

    public static (string Name, long Size, long LastWriteUtcTicks) Stat(string name, string? path)
    {
        var info = string.IsNullOrEmpty(path) ? null : new FileInfo(path);
        var (size, ticks) = info is { Exists: true } ? Of(info) : (-1L, 0L);
        return (name, size, ticks);
    }

    public static string SourceHash(IEnumerable<(string Name, long Size, long LastWriteUtcTicks)> plugins, string header)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(header));
        AppendLines(hash, plugins);
        return Sha256Hex.Hex(hash.GetHashAndReset(), 8);
    }

    public static string PluginsHash(IEnumerable<(string Name, long Size, long LastWriteUtcTicks)> plugins)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendLines(hash, plugins);
        return Sha256Hex.Hex(hash.GetHashAndReset());
    }

    // Invariant: an uncomputable current stamp must read as stale; only a missing recorded stamp has no opinion.
    public static bool Mismatch(string? recorded, string? current) =>
        string.IsNullOrEmpty(current)
        || (!string.IsNullOrEmpty(recorded) && !string.Equals(recorded, current, StringComparison.OrdinalIgnoreCase));
}
