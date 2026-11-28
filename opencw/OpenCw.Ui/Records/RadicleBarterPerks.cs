using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

// Requires: main thread; the player's perks are the PlayerPerks store, the vendor's are the base NPC_ record's own PRKR rows read through Radicle.
public sealed class RadicleBarterPerks(IRecordSource source, PlayerPerks player, string vendorBaseKey, IRecipeFacts? playerFacts) : IBarterPerkSource
{
    private readonly Dictionary<FormKey, PerkEntryPointsRecord?> _entries = new();
    private IReadOnlyList<HeldPerk>? _vendor;

    public IReadOnlyList<HeldPerk> Held(BarterActor actor) => actor == BarterActor.Player ? PlayerHeld() : (_vendor ??= VendorHeld());

    public IRecipeFacts? Facts(BarterActor actor) => actor == BarterActor.Player ? playerFacts : null;

    public PerkEntryPointsRecord? EntryPoints(FormKey perk)
    {
        if (_entries.TryGetValue(perk, out var cached)) return cached;
        PerkEntryPointsRecord? read;
        try
        {
            read = source.Environment.PerkEntryPoints(perk);
        }
        catch (RadicleException)
        {
            read = null;
        }
        _entries[perk] = read;
        return read;
    }

    private List<HeldPerk> PlayerHeld()
    {
        var held = new List<HeldPerk>();
        foreach (var (key, rank) in player.Ranks)
            if (rank > 0 && RecordKey.TryParse(key, out var plugin, out var local))
                held.Add(new HeldPerk(new FormKey(plugin, local), rank));
        return held;
    }

    private IReadOnlyList<HeldPerk> VendorHeld()
    {
        try
        {
            if (!RecordKey.TryParse(vendorBaseKey, out var plugin, out var local)
                || source.Environment.Winner(new FormKey(plugin, local)) is not { } npc)
                return [];
            return source.Environment.NpcGameplay(npc).Perks.Select(p => new HeldPerk(p.Perk, (int)Math.Min(p.Rank, int.MaxValue))).ToList();
        }
        catch (RadicleException)
        {
            return [];
        }
    }
}
