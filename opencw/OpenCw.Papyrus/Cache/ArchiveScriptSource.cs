using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Cache;

/// <summary>The archive reader the cache asks for script entries.</summary>
public interface IArchiveReader
{
    bool TryRead(string archivePath, string entryPath, out byte[] bytes);
}

/// <summary>One script taken out of an archive.</summary>
public sealed record ArchiveScript(string ArchivePath, string EntryPath, string ScriptName, int Bytes);

public sealed record ArchiveRefusal(string ArchivePath, string EntryPath, string Reason);

public sealed record ArchiveExtractionReport(
    IReadOnlyList<ArchiveScript> Scripts,
    IReadOnlyList<ArchiveRefusal> Refused)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"archive scripts={Scripts.Count} refused={Refused.Count}\n");
        foreach (var script in Scripts)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {script.ArchivePath}!{script.EntryPath} {script.ScriptName} {script.Bytes}\n");
        }
        foreach (var refusal in Refused)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  refused {refusal.ArchivePath}!{refusal.EntryPath} {refusal.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Reads the scripts a load order keeps inside BA2 archives.
/// </summary>
/// <remarks>
/// Radicle decodes the container, so this class never parses an archive itself: it asks an
/// <see cref="IArchiveReader"/> for one entry at a time and records the byte count it received. An entry
/// the reader cannot produce is refused and counted rather than reported as an empty script. A loose
/// script of the same name shadows the archived copy, which is the precedence the cache already records
/// under <c>cache.loose_precedence</c>. The whole pass is gated on its own evidence key, so an unproven
/// key reads nothing at all.
/// </remarks>
public sealed class ArchiveScriptSource
{
    public const string Key = "cache.archive_adapter";

    private readonly IArchiveReader _reader;
    private readonly EvidenceGate _gate;

    public ArchiveScriptSource(IArchiveReader reader, EvidenceGate gate)
    {
        _reader = reader;
        _gate = gate;
    }

    public int Read { get; private set; }

    public int Refused { get; private set; }

    /// <summary>
    /// Extracts the requested entries, shadowed by the loose script names the caller passes.
    /// </summary>
    public ArchiveExtractionReport Extract(
        IEnumerable<(string ArchivePath, string EntryPath)> requests,
        IEnumerable<string> looseScriptNames)
    {
        var loose = new HashSet<string>(looseScriptNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var scripts = new List<ArchiveScript>();
        var refused = new List<ArchiveRefusal>();

        if (!_gate.Gate(Key).Allowed)
        {
            foreach (var request in Ordered(requests))
            {
                Refused++;
                refused.Add(new ArchiveRefusal(request.ArchivePath, request.EntryPath,
                    "the archive extraction key is not proven, so nothing is read"));
            }
            return new ArchiveExtractionReport(scripts, refused);
        }

        foreach (var request in Ordered(requests))
        {
            var name = ScriptName(request.EntryPath);
            if (loose.Contains(name))
            {
                Refused++;
                refused.Add(new ArchiveRefusal(request.ArchivePath, request.EntryPath,
                    $"a loose script named {name} shadows the archived copy"));
                continue;
            }

            if (!_reader.TryRead(request.ArchivePath, request.EntryPath, out var bytes))
            {
                Refused++;
                refused.Add(new ArchiveRefusal(request.ArchivePath, request.EntryPath,
                    "the archive reader could not produce this entry, so it is not treated as an empty script"));
                continue;
            }

            Read++;
            scripts.Add(new ArchiveScript(request.ArchivePath, request.EntryPath, name, bytes.Length));
        }

        return new ArchiveExtractionReport(scripts, refused);
    }

    private static IEnumerable<(string ArchivePath, string EntryPath)> Ordered(
        IEnumerable<(string ArchivePath, string EntryPath)> requests) =>
        (requests ?? Array.Empty<(string, string)>())
            .OrderBy(request => request.ArchivePath, StringComparer.Ordinal)
            .ThenBy(request => request.EntryPath, StringComparer.Ordinal);

    private static string ScriptName(string entryPath)
    {
        var separator = entryPath.LastIndexOfAny(new[] { '/', '\\' });
        var name = separator >= 0 ? entryPath[(separator + 1)..] : entryPath;
        return name.EndsWith(".pex", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
