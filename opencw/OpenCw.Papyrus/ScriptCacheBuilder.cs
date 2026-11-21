using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using OpenCommonwealth.Services.Papyrus.Quest;

namespace OpenCommonwealth.Services.Papyrus;

public sealed record ScriptCacheBuildResult(
    ScriptCacheManifest Manifest,
    int ReplacedCount,
    IReadOnlyList<string> Unsupported,
    int ConvertedCount = 0,
    int ReusedCount = 0);

/// <summary>
/// Builds the machine-local script cache.
/// </summary>
/// <remarks>
/// A build resolves the winners, writes every winner's PEX payload and the manifest into an incoming directory,
/// then swaps directories. A winner whose source fingerprint and format match the previous cache reuses its PEX file;
/// every other winner is checked again. The swap is crash-safe: a marker is written first, the previous cache is
/// renamed aside rather than deleted, and <see cref="Recover"/> repairs whichever step an
/// interruption landed on. Because the whole directory is replaced, a script that is no longer
/// present in the load order does not survive a rebuild.
/// </remarks>
public static class ScriptCacheBuilder
{
    public static ScriptCacheBuildResult Build(
        IEnumerable<ScriptSourceCandidate> candidates,
        string dataFolder,
        string? root = null,
        IEnumerable<string>? unavailableSources = null,
        IReadOnlyList<QuestStartupInput>? questStartup = null,
        IReadOnlyList<string>? loadOrderRoots = null,
        IReadOnlyList<string>? loadOrderPlugins = null) =>
        Build(ScriptSelection.ResolveCandidates(candidates as IReadOnlyList<ScriptSourceCandidate> ?? candidates.ToList()),
            dataFolder, root, unavailableSources, questStartup, loadOrderRoots, loadOrderPlugins);

    public static ScriptCacheBuildResult Build(
        OpenedScriptSet scripts,
        string dataFolder,
        string? root = null,
        IReadOnlyList<QuestStartupInput>? questStartup = null,
        IReadOnlyList<string>? loadOrderRoots = null,
        IReadOnlyList<string>? loadOrderPlugins = null) =>
        Build(scripts.Winners, dataFolder, root, scripts.Unavailable, questStartup, loadOrderRoots, loadOrderPlugins);

    private static ScriptCacheBuildResult Build(
        IReadOnlyList<(string Name, ScriptSourceCandidate Winner, int Replaced)> winners,
        string dataFolder,
        string? root,
        IEnumerable<string>? unavailableSources,
        IReadOnlyList<QuestStartupInput>? questStartup,
        IReadOnlyList<string>? loadOrderRoots,
        IReadOnlyList<string>? loadOrderPlugins)
    {
        var cacheRoot = root ?? ScriptCacheLayout.DefaultRoot;
        var unsupported = (unavailableSources ?? Array.Empty<string>()).ToList();
        using var cacheLock = Lock(cacheRoot);
        RecoverUnderLock(cacheRoot);
        var previous = Reusable(cacheRoot);
        var previousEntries = new Dictionary<string, ScriptCacheEntry>(StringComparer.Ordinal);
        foreach (var entry in previous?.Entries ?? Array.Empty<ScriptCacheEntry>())
        {
            previousEntries.TryAdd(entry.Name, entry);
        }
        var currentDirectory = ScriptCacheLayout.CurrentDirectory(cacheRoot);

        Directory.CreateDirectory(cacheRoot);
        var incoming = ScriptCacheLayout.IncomingDirectory(cacheRoot);
        var scriptsRoot = ScriptCacheLayout.ScriptsDirectoryIn(incoming);
        Delete(incoming);
        Delete(ScriptCacheLayout.ObsoleteDirectory(cacheRoot));
        Directory.CreateDirectory(scriptsRoot);

        var entries = new List<ScriptCacheEntry>(winners.Count);
        var loose = 0;
        var archive = 0;
        var replaced = 0;
        var converted = 0;
        var reused = 0;
        foreach (var (name, winner, replacedCount) in winners)
        {
            replaced += replacedCount;
            var payload = winner.Read();
            var fingerprint = Fingerprint(payload);
            var target = ScriptCacheLayout.PexPathIn(incoming, name);
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var cachedPex = ScriptCacheLayout.PexPathIn(currentDirectory, name);
            byte[] pexBytes;
            if (previousEntries.TryGetValue(name, out var entry) && entry.Sha256 == fingerprint && File.Exists(cachedPex))
            {
                pexBytes = File.ReadAllBytes(cachedPex);
                if (Fingerprint(pexBytes) != entry.PexSha256)
                    throw new InvalidOperationException($"the cached PEX for {name} does not match its manifest fingerprint; reconvert assets into a fresh cache");
                reused++;
            }
            else
            {
                try
                {
                    // Requires: ReadCode runs for every object, so a payload the host cannot decode is refused here.
                    ObjectNames(payload);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    unsupported.Add($"{name}: {failure.Message}");
                    continue;
                }
                pexBytes = payload;
                converted++;
            }
            File.WriteAllBytes(target, pexBytes);
            if (winner.Kind == ScriptSourceKind.Loose)
            {
                loose++;
            }
            else
            {
                archive++;
            }
            entries.Add(new ScriptCacheEntry(name, KindName(winner.Kind), winner.ContainerPath,
                winner.LoadIndex, payload.LongLength, fingerprint, ScriptCacheLayout.PexRelativePath(name),
                Fingerprint(pexBytes)));
        }

        var startupBytes = QuestStartupDatabase.ToBytes(questStartup ?? Array.Empty<QuestStartupInput>());
        var startup = QuestStartupDatabase.FromJson(Encoding.UTF8.GetString(startupBytes));
        File.WriteAllBytes(Path.Combine(incoming, ScriptCacheLayout.QuestStartupFileName), startupBytes);
        var manifest = new ScriptCacheManifest(
            ScriptCacheManifest.CurrentFormatVersion,
            RadiclePin.Repository,
            RadiclePin.Commit,
            RadiclePin.AbiVersion,
            dataFolder,
            entries.Count,
            loose,
            archive,
            replaced,
            unsupported,
            entries,
            Fingerprint(startupBytes),
            startup.Inputs.Count,
            NormalizeRoots(loadOrderRoots),
            SourceSetFingerprint(entries),
            NormalizePlugins(loadOrderPlugins),
            PluginStatLines(loadOrderPlugins));
        File.WriteAllText(Path.Combine(incoming, ScriptCacheLayout.ManifestFileName), manifest.ToJson(),
            new UTF8Encoding(false));

        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var source = $"{entry.ContainerPath}:{entry.Name}";
            foreach (var script in ObjectNames(File.ReadAllBytes(ScriptCacheLayout.PexPathIn(incoming, entry.Name))))
            {
                if (sources.TryGetValue(script, out var first))
                {
                    throw new InvalidOperationException($"the script {script} is already in the library; sources: {first} / {source}");
                }
                sources.Add(script, source);
            }
        }
        Swap(cacheRoot);
        return new ScriptCacheBuildResult(manifest, replaced, unsupported, converted, reused);
    }

    private static ScriptCacheManifest? Reusable(string cacheRoot)
    {
        ScriptCacheManifest? previous;
        try
        {
            previous = Load(cacheRoot);
        }
        catch (Exception failure) when (failure is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
        return previous is not null
               && previous.FormatVersion == ScriptCacheManifest.CurrentFormatVersion
               && previous.MatchesRadiclePin()
            ? previous
            : null;
    }

    /// <summary>Reads the current manifest, or null when no cache has been built.</summary>
    public static ScriptCacheManifest? Load(string? root = null)
    {
        var path = ScriptCacheLayout.ManifestPath(root ?? ScriptCacheLayout.DefaultRoot);
        return File.Exists(path) ? ScriptCacheManifest.FromJson(File.ReadAllText(path)) : null;
    }

    /// <summary>
    /// Repairs the cache after an interrupted build. Safe to call at any time, including when no
    /// cache exists yet, and it never leaves both a current and an incoming directory behind.
    /// </summary>
    public static void Recover(string? root = null)
    {
        var cacheRoot = root ?? ScriptCacheLayout.DefaultRoot;
        using var cacheLock = Lock(cacheRoot);
        RecoverUnderLock(cacheRoot);
    }

    // Invariant: every writer and every recovery holds this cross-process lock, so a second process never sees a live incoming directory or swap marker as an interrupted one.
    public static IDisposable Lock(string cacheRoot, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(cacheRoot);
        var path = Path.Combine(cacheRoot, ScriptCacheLayout.LockFileName);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(10));
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void RecoverUnderLock(string cacheRoot)
    {
        var marker = ScriptCacheLayout.MarkerPath(cacheRoot);
        var current = ScriptCacheLayout.CurrentDirectory(cacheRoot);
        var obsolete = ScriptCacheLayout.ObsoleteDirectory(cacheRoot);

        if (!File.Exists(marker))
        {
            Delete(ScriptCacheLayout.IncomingDirectory(cacheRoot));
            Delete(obsolete);
            return;
        }

        if (!Directory.Exists(current) && Directory.Exists(obsolete))
        {
            Directory.Move(obsolete, current);
        }
        Delete(ScriptCacheLayout.IncomingDirectory(cacheRoot));
        Delete(obsolete);
        File.Delete(marker);
    }

    public static string KindName(ScriptSourceKind kind) =>
        kind == ScriptSourceKind.Loose ? "loose" : "archive";

    public static byte[] ReadQuestStartupFromScriptCache(string cacheRoot, ScriptCacheManifest manifest)
    {
        var scriptBytes = File.ReadAllBytes(ScriptCacheLayout.QuestStartupPath(cacheRoot));
        if (Fingerprint(scriptBytes) != manifest.QuestStartupSha256)
            throw new InvalidOperationException("the converted quest startup payload does not match the script cache manifest");
        return scriptBytes;
    }

    public static string Fingerprint(byte[] payload) => Sha256Hex.Of(payload);

    public static IReadOnlyList<string> NormalizeRoots(IReadOnlyList<string>? roots) =>
        (roots ?? Array.Empty<string>())
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> NormalizePlugins(IReadOnlyList<string>? plugins) =>
        (plugins ?? Array.Empty<string>())
            .Where(plugin => !string.IsNullOrWhiteSpace(plugin))
            .Select(plugin => Path.GetFileName(plugin.Trim()))
            .ToList();

    public static IReadOnlyList<string> PluginStatLines(IReadOnlyList<string>? plugins) =>
        (plugins ?? Array.Empty<string>())
            .Where(plugin => !string.IsNullOrWhiteSpace(plugin))
            .Select(plugin =>
            {
                var path = plugin.Trim();
                var info = new FileInfo(path);
                var exists = info.Exists;
                return $"{Path.GetFileName(path).ToLowerInvariant()}|{(exists ? info.Length : -1)}|{(exists ? info.LastWriteTimeUtc.Ticks : 0)}";
            })
            .ToList();

    public static string SourceSetFingerprint(IEnumerable<ScriptCacheEntry> entries)
    {
        const char unit = (char)31;
        const char row = (char)10;
        var text = new StringBuilder();
        foreach (var entry in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            text.Append(entry.Name).Append(unit).Append(entry.Kind).Append(unit).Append(entry.ContainerPath)
                .Append(unit).Append(entry.LoadIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(unit).Append(entry.Sha256).Append(row);
        }
        return Fingerprint(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private static List<string> ObjectNames(byte[] payload)
    {
        using var pex = Radicle.Interop.PexFile.FromBytes(payload);
        var names = new List<string>(pex.Objects.Count);
        for (var index = 0; index < pex.Objects.Count; ++index)
        {
            pex.ReadCode(index);
            names.Add(Encoding.Latin1.GetString(pex.Objects[index].Name));
        }
        return names;
    }

    private static void Swap(string cacheRoot)
    {
        var current = ScriptCacheLayout.CurrentDirectory(cacheRoot);
        var incoming = ScriptCacheLayout.IncomingDirectory(cacheRoot);
        var obsolete = ScriptCacheLayout.ObsoleteDirectory(cacheRoot);

        File.WriteAllText(ScriptCacheLayout.MarkerPath(cacheRoot), "swapping", new UTF8Encoding(false));
        if (Directory.Exists(current))
        {
            Directory.Move(current, obsolete);
        }
        Directory.Move(incoming, current);
        Delete(obsolete);
        File.Delete(ScriptCacheLayout.MarkerPath(cacheRoot));
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
