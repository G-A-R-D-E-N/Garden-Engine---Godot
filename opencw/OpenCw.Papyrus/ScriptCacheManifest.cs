using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCommonwealth.Services.Papyrus;

/// <summary>One cached script: where it came from and the fingerprint of its source payload, never the payload.</summary>
public sealed record ScriptCacheEntry(
    string Name,
    string Kind,
    string ContainerPath,
    int LoadIndex,
    long ByteSize,
    string Sha256,
    string PexFile = "",
    string PexSha256 = "");

/// <summary>
/// The cache manifest.
/// </summary>
/// <remarks>
/// The manifest is machine-local cache metadata. It records the winner of every script name, its
/// provenance and its fingerprint, so a rebuild can be compared without reading payloads. It
/// deliberately never stores payload bytes: those are separate files under the cache's scripts
/// directory, and both stay outside the repository and any release package.
/// </remarks>
public sealed record ScriptCacheManifest(
    int FormatVersion,
    string RadicleRepository,
    string RadicleCommit,
    int RadicleAbiVersion,
    string DataFolder,
    int ScriptCount,
    int LooseCount,
    int ArchiveCount,
    int ReplacedCount,
    IReadOnlyList<string> Unsupported,
    IReadOnlyList<ScriptCacheEntry> Entries,
    string QuestStartupSha256 = "",
    int QuestStartupCount = 0,
    IReadOnlyList<string>? LoadOrderRoots = null,
    string SourceSetSha256 = "",
    IReadOnlyList<string>? LoadOrderPlugins = null,
    IReadOnlyList<string>? LoadOrderPluginStats = null)
{
    public const int CurrentFormatVersion = 9;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ScriptCacheManifest FromJson(string json) =>
        JsonSerializer.Deserialize<ScriptCacheManifest>(json, Options)
        ?? throw new InvalidOperationException("the script cache manifest was empty");

    public ScriptCacheEntry? Find(string name) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal));

    public bool MatchesLoadOrder(IReadOnlyList<string> roots) => SameNames(LoadOrderRoots, roots);

    public bool MatchesPluginOrder(IReadOnlyList<string> plugins) => SameNames(LoadOrderPlugins, ScriptCacheBuilder.NormalizePlugins(plugins));

    public bool MatchesPluginStats(IReadOnlyList<string> plugins)
    {
        var expected = ScriptCacheBuilder.PluginStatLines(plugins);
        return LoadOrderPluginStats is { } recorded && recorded.SequenceEqual(expected, StringComparer.Ordinal);
    }

    private static bool SameNames(IReadOnlyList<string>? recorded, IReadOnlyList<string> expected) =>
        recorded is not null && recorded.Count == expected.Count
        && recorded.Zip(expected, (left, right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase)).All(equal => equal);

    public bool MatchesRadiclePin() =>
        RadicleRepository == RadiclePin.Repository
        && RadicleCommit == RadiclePin.Commit
        && RadicleAbiVersion == RadiclePin.AbiVersion;

    public string? DescribeMismatch()
    {
        var parts = new List<string>();
        if (FormatVersion != CurrentFormatVersion)
            parts.Add($"format {FormatVersion} (expected {CurrentFormatVersion})");
        if (ScriptCount != Entries.Count)
            parts.Add($"count {ScriptCount} (entries {Entries.Count})");
        if (RadicleRepository != RadiclePin.Repository)
            parts.Add($"repository {RadicleRepository} (pin {RadiclePin.Repository})");
        if (RadicleAbiVersion != RadiclePin.AbiVersion)
            parts.Add($"ABI {RadicleAbiVersion} (pin {RadiclePin.AbiVersion})");
        if (RadicleCommit != RadiclePin.Commit)
            parts.Add($"commit {RadicleCommit} (pin {RadiclePin.Commit})");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
