using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed class RadicleVendorLookup : IVendorRecords
{
    public const string Master = "Fallout4.esm";

    public const string MerchantContainerPath = "FACT\\MerchantContainer";

    public static readonly IReadOnlyList<string> KeywordEditorIds =
        ["VendorContainerKeyword01", "VendorContainerKeyword02", "VendorContainerKeyword03", "VendorContainerKeyword04", "VendorContainerKeyword05", "VendorContainerKeyword06"];

    // Invariant: fallback only; VendorKeywords reads the KYWD records by KeywordEditorIds first and uses these Fallout4.esm ids when the load order lacks any of the six.
    public static readonly IReadOnlyList<uint> VendorKeywordIds = [0x10FCD1, 0x1102EE, 0x1102EF, 0x1102FD, 0x1BBC94, 0x2269A0];

    public static readonly IReadOnlySet<string> Keywords =
        VendorKeywordIds.Select(id => RecordKey.Format(Master, id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private readonly IRecordReader _reader;
    private readonly IRecordSource _source;
    private IReadOnlySet<string>? _keywords;

    public RadicleVendorLookup(IRecordSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _reader = new RadicleRecordReader(source);
    }

    // Requires: the environment stays valid for the call; a failed read keeps the static set; the set is resolved once on first use and never when the lookup is only built.
    public IReadOnlySet<string> VendorKeywords => _keywords ??= ResolveKeywords(_source.Environment);

    internal static IReadOnlySet<string> ResolveKeywords(Radicle.Interop.RadicleEnvironment env)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var editorId in KeywordEditorIds)
                if (env.FindEditorId(editorId, "KYWD") is { } record) found.Add(RecordKey.Format(env.Info(record).FormKey));
        }
        catch (Radicle.Interop.RadicleException) { return Keywords; }
        return found.Count == KeywordEditorIds.Count ? found : Keywords;
    }

    public IEnumerable<(string Keyword, string Reference)> LinkedRefs(string npcRefKey) => RecordRows.LinkedRefs(_reader, npcRefKey);

    public IEnumerable<string?> FactionChests(string npcBaseKey) => Chests(_reader, npcBaseKey);

    // Requires: NPC_\Faction lists the NPC's own SNAM links; an empty link (null or misfit slot) makes the whole list unreadable, so no chests, as the typed decode threw.
    internal static List<string> Chests(IRecordReader reader, string npcBaseKey)
    {
        var chests = new List<string>();
        if (reader.List(npcBaseKey, "NPC_\\Faction") is not { } factions) return chests;
        foreach (var text in factions)
        {
            if (RecordRows.Format(text) is not { } faction) return [];
            if (RecordRows.Link(reader, faction, MerchantContainerPath) is { } chest) chests.Add(chest);
        }
        return chests;
    }
}
