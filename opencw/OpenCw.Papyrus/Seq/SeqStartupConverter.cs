using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Seq;

public enum SeqInputState
{
    /// <summary>Radicle decoded the file and handed its raw FormIDs over.</summary>
    Decoded,

    /// <summary>The file could not be read.</summary>
    NotRead,
}

public sealed record SeqInput(string Path, string Plugin, SeqInputState State, IReadOnlyList<uint> FormIds);

/// <summary>One quest the runtime should start, with the raw FormID the SEQ carried.</summary>
public sealed record QuestStartupEntry(string Plugin, uint RawFormId);

public sealed record SeqStartupReport(
    int Files,
    int Decoded,
    int NotRead,
    int Empty,
    int Entries,
    IReadOnlyList<string> ApproximationReasons,
    IReadOnlyList<string> NotReadFiles,
    IReadOnlyList<QuestStartupEntry> Startup)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"files={Files} decoded={Decoded} not-read={NotRead} empty={Empty} entries={Entries} approximations={ApproximationReasons.Count}\n");
        foreach (var reason in ApproximationReasons)
        {
            text.Append(CultureInfo.InvariantCulture, $"  approximation {reason}\n");
        }
        foreach (var file in NotReadFiles)
        {
            text.Append(CultureInfo.InvariantCulture, $"  not-read {file}\n");
        }
        foreach (var entry in Startup)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  start {entry.Plugin} {entry.RawFormId.ToString("X8", CultureInfo.InvariantCulture)}\n");
        }
        return text.ToString();
    }
}

/// <summary>
/// Turns the effective SEQ inputs into Garden-native startup data.
/// </summary>
/// <remarks>
/// The converter never touches SEQ bytes: Radicle decodes the container and hands over the raw
/// FormIDs, and this type only decides what Garden may do with them. Every decision is gated by the
/// evidence registry, so a behavior that is not proven becomes a reported approximation rather than a
/// guess: an empty file, more than one plugin contributing entries, and the unresolved runtime
/// meaning of a master index are all recorded. A master index is never resolved here — the raw FormID
/// is kept exactly as the file carried it.
/// </remarks>
public static class SeqStartupConverter
{
    public const string ArrayKey = "seq.container.array_of_formids";
    public const string EmptyFileKey = "seq.empty_file";
    public const string PrecedenceKey = "seq.precedence";
    public const string MasterIndexKey = "seq.runtime_master_index_resolution";

    public const string ContainerNotProven = "the SEQ container decode is not proven, so no startup data was produced";
    public const string EmptyFileReason = "an empty .seq was skipped because the engine's behavior for it is unproven";
    public const string PrecedenceReason = "precedence between plugins is unproven, so entries are kept per plugin in file order and never merged";
    public const string MasterIndexReason = "the runtime meaning of a master index is unproven, so the raw FormID is kept unresolved";

    public static SeqStartupReport Convert(IEnumerable<SeqInput> inputs, EvidenceGate gate)
    {
        if (!gate.Gate(ArrayKey).Allowed)
        {
            return new SeqStartupReport(0, 0, 0, 0, 0, new[] { ContainerNotProven },
                Array.Empty<string>(), Array.Empty<QuestStartupEntry>());
        }

        var ordered = inputs
            .OrderBy(input => input.Plugin, StringComparer.Ordinal)
            .ThenBy(input => input.Path, StringComparer.Ordinal)
            .ToList();
        var reasons = new List<string>();
        var notRead = new List<string>();
        var startup = new List<QuestStartupEntry>();
        var decoded = 0;
        var empty = 0;
        var contributors = new HashSet<string>(StringComparer.Ordinal);
        var masterIndexReported = false;

        foreach (var input in ordered)
        {
            if (input.State == SeqInputState.NotRead)
            {
                notRead.Add(input.Path);
                continue;
            }
            if (input.FormIds.Count == 0)
            {
                empty++;
                if (!gate.Gate(EmptyFileKey).Allowed && !reasons.Contains(EmptyFileReason))
                {
                    reasons.Add(EmptyFileReason);
                }
                continue;
            }
            decoded++;
            contributors.Add(input.Plugin);
            foreach (var formId in input.FormIds)
            {
                startup.Add(new QuestStartupEntry(input.Plugin, formId));
            }
            if (!masterIndexReported && !gate.Gate(MasterIndexKey).Allowed)
            {
                reasons.Add(MasterIndexReason);
                masterIndexReported = true;
            }
        }

        if (contributors.Count > 1 && !gate.Gate(PrecedenceKey).Allowed)
        {
            reasons.Add(PrecedenceReason);
        }

        return new SeqStartupReport(ordered.Count, decoded, notRead.Count, empty, startup.Count, reasons,
            notRead, startup);
    }

    /// <summary>Writes the startup data, replacing the previous file under a rename swap.</summary>
    public static void Write(SeqStartupReport report, string path)
    {
        Recover(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var incoming = path + ".incoming";
        var obsolete = path + ".obsolete";
        File.WriteAllText(incoming, Serialize(report), new UTF8Encoding(false));
        if (File.Exists(path))
        {
            if (File.Exists(obsolete))
            {
                File.Delete(obsolete);
            }
            File.Move(path, obsolete);
            File.Move(incoming, path);
            File.Delete(obsolete);
            return;
        }
        File.Move(incoming, path);
    }

    public static void Recover(string path)
    {
        var incoming = path + ".incoming";
        var obsolete = path + ".obsolete";
        if (!File.Exists(path) && File.Exists(obsolete))
        {
            File.Move(obsolete, path);
        }
        if (File.Exists(incoming))
        {
            File.Delete(incoming);
        }
        if (File.Exists(obsolete))
        {
            File.Delete(obsolete);
        }
    }

    public static string Serialize(SeqStartupReport report) =>
        JsonSerializer.Serialize(new
        {
            files = report.Files,
            decoded = report.Decoded,
            notRead = report.NotRead,
            empty = report.Empty,
            entries = report.Entries,
            approximations = report.ApproximationReasons,
            notReadFiles = report.NotReadFiles,
            startup = report.Startup.Select(entry => new { plugin = entry.Plugin, rawFormId = entry.RawFormId }),
        }, new JsonSerializerOptions { WriteIndented = true });
}
