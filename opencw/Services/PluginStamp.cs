using System;
using System.Collections.Concurrent;
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

    private static readonly ConcurrentDictionary<(string Path, long Size, long Ticks), string> Digests = new();

    public static void ResetDigestMemo() => Digests.Clear();

    // Invariant: a readable plugin always carries its content digest; an unreadable one throws so callers read the stamp as uncomputable.
    public static (string Name, long Size, long LastWriteUtcTicks) StatWithContent(string name, string? path)
    {
        var (statName, size, ticks) = Stat(name, path);
        if (size < 0) return (statName, size, ticks);
        var digest = Digests.GetOrAdd((Path.GetFullPath(path!).ToLowerInvariant(), size, ticks), _ =>
        {
            using var stream = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            return Sha256Hex.OfStream(stream, 16);
        });
        return ($"{statName}#{digest}", size, ticks);
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
