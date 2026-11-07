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
    string IrSha256 = "",
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
    int IrFormatVersion,
    int LoweringVersion,
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
    int QuestStartupCount = 0)
{
    public const int CurrentFormatVersion = 5;

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

    public bool MatchesRadiclePin() =>
        RadicleRepository == RadiclePin.Repository
        && RadicleCommit == RadiclePin.Commit
        && RadicleAbiVersion == RadiclePin.AbiVersion;
}
