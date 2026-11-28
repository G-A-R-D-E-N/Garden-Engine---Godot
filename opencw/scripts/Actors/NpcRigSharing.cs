using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Radicle.Interop;
using RadicleKey = Radicle.Interop.FormKey;

namespace OpenCommonwealth;

public static class NpcRigSharing
{
    public const int ProbeSeeds = 32;
    public const string CacheFolder = "npc6";
    private static readonly HashSet<string> Dependent = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex NamedScene = new(@"^[0-9A-F]{8}_.+\.tscn$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SeededScene = new(@"_[0-9A-F]{16}\.tscn$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Stem(RadicleKey key) => $"{key.LocalId:X8}_{key.Plugin.ToLowerInvariant()}";

    public static string SharedPath(RadicleKey key) => AssetCachePaths.Resolve($"{CacheFolder}/{Stem(key)}_shared.tscn");

    public static string SeededPath(RadicleKey key, ulong seed) => AssetCachePaths.Resolve($"{CacheFolder}/{Stem(key)}_{seed:X16}.tscn");

    public static string LegacyPath(RadicleKey key) => AssetCachePaths.Resolve($"{CacheFolder}/{Stem(key)}.tscn");

    public static void Reset() => Dependent.Clear();

    public static bool IsDependent(string baseKey) => Dependent.Contains(baseKey);

    public static bool IsShared(string baseKey, NpcVisual first, Func<ulong, NpcVisual> decode)
    {
        if (Dependent.Contains(baseKey)) return false;
        for (int i = 1; i <= ProbeSeeds; i++)
            if (!SameVisual(first, decode(ProbeSeed(i))))
            {
                Dependent.Add(baseKey);
                return false;
            }
        return true;
    }

    public static ulong ProbeSeed(int index)
    {
        ulong z = (ulong)index * 0x9E3779B97F4A7C15UL + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static bool SameVisual(NpcVisual a, NpcVisual b) =>
        a.Npc == b.Npc && a.Race == b.Race && a.RaceEditorId == b.RaceEditorId && a.Female == b.Female
        && SameTint(a.SkinTint, b.SkinTint) && a.SkeletonHkxEntry == b.SkeletonHkxEntry && a.IdleClipEntry == b.IdleClipEntry
        && a.Parts.SequenceEqual(b.Parts) && a.Stubs.SequenceEqual(b.Stubs);

    private static bool SameTint(float[]? a, float[]? b) => a == null ? b == null : b != null && a.SequenceEqual(b);

    public static int CleanLegacy()
    {
        int removed = 0;
        foreach (var file in SceneFiles("*.tscn"))
        {
            string name = Path.GetFileName(file);
            bool legacy = NamedScene.IsMatch(name) && !name.EndsWith("_shared.tscn", StringComparison.OrdinalIgnoreCase) && !SeededScene.IsMatch(name);
            if (legacy && TryDelete(file)) removed++;
        }
        return removed;
    }

    public static int RetireSeeded(RadicleKey key)
    {
        var seeded = new Regex("^" + Regex.Escape(Stem(key)) + @"_[0-9A-F]{16}\.tscn$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int removed = 0;
        foreach (var file in SceneFiles(Stem(key) + "_*.tscn"))
            if (seeded.IsMatch(Path.GetFileName(file)) && TryDelete(file)) removed++;
        return removed;
    }

    public static bool RetireShared(RadicleKey key) => CacheFileExists(SharedPath(key)) && TryDelete(ProjectSettings.GlobalizePath(SharedPath(key)));

    private static bool CacheFileExists(string path) => File.Exists(ProjectSettings.GlobalizePath(path));

    private static string[] SceneFiles(string pattern)
    {
        var dir = ProjectSettings.GlobalizePath(AssetCachePaths.Resolve(CacheFolder));
        return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>();
    }

    private static bool TryDelete(string file)
    {
        try { File.Delete(file); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"[NpcSpawn] could not delete the stale NPC scene {file}: {ex.Message}");
            return false;
        }
    }
}
