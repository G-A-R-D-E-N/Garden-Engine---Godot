using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed class RadicleVendorLookup : IVendorRecords
{
    public const string Master = "Fallout4.esm";

    public const string MerchantContainerPath = "FACT\\MerchantContainer";

    public static readonly IReadOnlyList<uint> VendorKeywordIds = [0x10FCD1, 0x1102EE, 0x1102EF, 0x1102FD, 0x1BBC94, 0x2269A0];

    public static readonly IReadOnlySet<string> Keywords =
        VendorKeywordIds.Select(id => RecordKey.Format(Master, id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private readonly IRecordReader _reader;

    public RadicleVendorLookup(IRecordSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _reader = new RadicleRecordReader(source);
    }

    public IReadOnlySet<string> VendorKeywords => Keywords;

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
