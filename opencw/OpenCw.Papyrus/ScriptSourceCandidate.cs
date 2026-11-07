using System;
using System.Collections.Generic;
using System.IO;

namespace OpenCommonwealth.Services.Papyrus;

/// <summary>Where a script payload came from.</summary>
public enum ScriptSourceKind
{
    /// <summary>Inside an archive. Loses to a loose file with the same name.</summary>
    Archive = 0,

    /// <summary>A loose file under a Data tree. Wins over an archive with the same name.</summary>
    Loose = 1,
}

/// <summary>One place a script payload was found, with a reader for its bytes.</summary>
public sealed record ScriptSourceCandidate(
    string Name,
    ScriptSourceKind Kind,
    string ContainerPath,
    int LoadIndex,
    long ByteSize,
    Func<byte[]> Read);

/// <summary>Whether a payload source could be used, and why not when it could not.</summary>
public sealed record ScriptSourceAvailability(bool Available, string Reason);

/// <summary>
/// Loose scripts under a Data tree: Data/Scripts/**/*.pex. A higher load index wins.
/// </summary>
public static class LooseScriptSource
{
    public const string ScriptsFolderName = "Scripts";

    public static IEnumerable<ScriptSourceCandidate> Enumerate(
        IEnumerable<(string DataRoot, int LoadIndex)> roots)
    {
        foreach (var (dataRoot, loadIndex) in roots)
        {
            var scriptsRoot = Path.Combine(dataRoot, ScriptsFolderName);
            if (!Directory.Exists(scriptsRoot))
            {
                continue;
            }
            foreach (var path in Directory.EnumerateFiles(scriptsRoot, "*", SearchOption.AllDirectories))
            {
                if (!path.EndsWith(ScriptCacheLayout.PexExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var name = ScriptCachePrecedence.NormalizeName(Path.GetRelativePath(scriptsRoot, path));
                var info = new FileInfo(path);
                yield return new ScriptSourceCandidate(name, ScriptSourceKind.Loose, path, loadIndex,
                    info.Length, () => File.ReadAllBytes(path));
            }
        }
    }
}

/// <summary>
/// Archive scripts: the prefix, extension and naming rules the cache expects, with the caller
/// supplying the entries and the reader.
/// </summary>
/// <remarks>
/// This library never opens an archive itself. Radicle is the only archive parser the project allows,
/// and this library stays free of `Radicle.Interop` so its tests run without the native build, so the
/// caller supplies the entry list and a reader for one entry's bytes. The selection rules live here
/// where they can be tested without an archive; the reading stays with Radicle. The reader is called
/// with the entry's position in the supplied list, and only when a payload is actually wanted.
/// </remarks>
public static class ArchiveScriptSource
{
    public const string ScriptsPrefix = "Scripts/";

    public static ScriptSourceAvailability Availability { get; } = new(
        true,
        "archive entries are selected here and read through the caller's Radicle archive reader");

    public static IEnumerable<ScriptSourceCandidate> Enumerate(
        string archivePath,
        int loadIndex,
        IEnumerable<(string EntryPath, long ByteSize)> entries,
        Func<int, byte[]> read)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }
        if (read is null)
        {
            throw new ArgumentNullException(nameof(read));
        }

        var position = -1;
        foreach (var (entryPath, byteSize) in entries)
        {
            position++;
            if (ScriptName(entryPath) is not { } name)
            {
                continue;
            }
            var wanted = position;
            yield return new ScriptSourceCandidate(name, ScriptSourceKind.Archive, archivePath, loadIndex,
                byteSize, () => read(wanted));
        }
    }

    /// <summary>The normalized script name an archive entry provides, or null when it provides none.</summary>
    public static string? ScriptName(string entryPath)
    {
        if (string.IsNullOrEmpty(entryPath))
        {
            return null;
        }
        var normalized = entryPath.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(ScriptsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var relative = normalized[ScriptsPrefix.Length..];
        if (relative.Length == 0
            || !relative.EndsWith(ScriptCacheLayout.PexExtension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        try
        {
            return ScriptCachePrecedence.NormalizeName(ScriptCacheLayout.SafeName(relative));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
