using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus;

public sealed record ScriptCacheBuildResult(
    ScriptCacheManifest Manifest,
    int ReplacedCount,
    IReadOnlyList<string> Unsupported,
    int ConvertedCount = 0,
    int ReusedCount = 0);

/// <summary>Turns one source payload into the IR text the cache stores, with the versions that key it.</summary>
public sealed record ScriptCacheConverter(int IrFormatVersion, int LoweringVersion, Func<byte[], string> Convert);

/// <summary>
/// Builds the machine-local script cache.
/// </summary>
/// <remarks>
/// A build resolves the winners, writes every winner's IR and the manifest into an incoming directory,
/// then swaps directories. A winner whose source fingerprint and versions match the previous cache
/// reuses its IR file; every other winner is converted. The swap is crash-safe: a marker is written first, the previous cache is
/// renamed aside rather than deleted, and <see cref="Recover"/> repairs whichever step an
/// interruption landed on. Because the whole directory is replaced, a script that is no longer
/// present in the load order does not survive a rebuild.
/// </remarks>
public static class ScriptCacheBuilder
{
    public static ScriptCacheBuildResult Build(
        IEnumerable<ScriptSourceCandidate> candidates,
        string dataFolder,
        ScriptCacheConverter converter,
        string? root = null,
        IEnumerable<string>? unavailableSources = null,
        IReadOnlyList<QuestStartupInput>? questStartup = null)
    {
        var cacheRoot = root ?? ScriptCacheLayout.DefaultRoot;
        var unsupported = (unavailableSources ?? Array.Empty<string>()).ToList();
        Recover(cacheRoot);
        var previous = Reusable(cacheRoot, converter);
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

        var winners = ScriptCachePrecedence.Resolve(candidates);
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
            var target = ScriptCacheLayout.ScriptPathIn(incoming, name);
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var cached = ScriptCacheLayout.ScriptPathIn(currentDirectory, name);
            var cachedPex = ScriptCacheLayout.PexPathIn(currentDirectory, name);
            byte[] irBytes;
            byte[] pexBytes;
            if (previousEntries.TryGetValue(name, out var entry) && entry.Sha256 == fingerprint
                && File.Exists(cached) && File.Exists(cachedPex))
            {
                irBytes = File.ReadAllBytes(cached);
                if (Fingerprint(irBytes) != entry.IrSha256)
                    throw new InvalidOperationException($"the cached IR for {name} does not match its manifest fingerprint; reconvert assets into a fresh cache");
                pexBytes = File.ReadAllBytes(cachedPex);
                if (Fingerprint(pexBytes) != entry.PexSha256)
                    throw new InvalidOperationException($"the cached PEX for {name} does not match its manifest fingerprint; reconvert assets into a fresh cache");
                reused++;
            }
            else
            {
                string ir;
                try
                {
                    ir = converter.Convert(payload);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    unsupported.Add($"{name}: {failure.Message}");
                    continue;
                }
                irBytes = Encoding.UTF8.GetBytes(ir);
                pexBytes = payload;
                converted++;
            }
            File.WriteAllBytes(target, irBytes);
            File.WriteAllBytes(ScriptCacheLayout.PexPathIn(incoming, name), pexBytes);
            if (winner.Kind == ScriptSourceKind.Loose)
            {
                loose++;
            }
            else
            {
                archive++;
            }
            entries.Add(new ScriptCacheEntry(name, KindName(winner.Kind), winner.ContainerPath,
                winner.LoadIndex, payload.LongLength, fingerprint, Fingerprint(irBytes),
                ScriptCacheLayout.PexRelativePath(name), Fingerprint(pexBytes)));
        }

        var startup = new QuestStartupDatabase(QuestStartupDatabase.CurrentFormatVersion,
            questStartup ?? Array.Empty<QuestStartupInput>());
        var startupBytes = Encoding.UTF8.GetBytes(startup.ToJson());
        QuestStartupDatabase.FromJson(Encoding.UTF8.GetString(startupBytes));
        File.WriteAllBytes(Path.Combine(incoming, ScriptCacheLayout.QuestStartupFileName), startupBytes);
        var manifest = new ScriptCacheManifest(
            ScriptCacheManifest.CurrentFormatVersion,
            converter.IrFormatVersion,
            converter.LoweringVersion,
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
            startup.Inputs.Count);
        File.WriteAllText(Path.Combine(incoming, ScriptCacheLayout.ManifestFileName), manifest.ToJson(),
            new UTF8Encoding(false));

        var library = new ScriptLibrary();
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            foreach (var script in PapyrusIr.FromJson(File.ReadAllText(ScriptCacheLayout.ScriptPathIn(incoming, entry.Name))))
            {
                var source = $"{entry.ContainerPath}:{entry.Name}";
                try
                {
                    library.Add(script);
                }
                catch (InvalidOperationException failure)
                {
                    throw new InvalidOperationException($"{failure.Message}; sources: {sources[script.Name]} / {source}", failure);
                }
                sources.Add(script.Name, source);
            }
        Swap(cacheRoot);
        return new ScriptCacheBuildResult(manifest, replaced, unsupported, converted, reused);
    }

    private static ScriptCacheManifest? Reusable(string cacheRoot, ScriptCacheConverter converter)
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
               && previous.IrFormatVersion == converter.IrFormatVersion
               && previous.LoweringVersion == converter.LoweringVersion
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

    public static string Fingerprint(byte[] payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

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
