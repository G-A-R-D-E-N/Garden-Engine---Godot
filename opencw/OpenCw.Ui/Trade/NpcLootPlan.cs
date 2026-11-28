using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed record NpcLootRuns(IReadOnlyList<InventoryStack> Carried, IReadOnlyList<InventoryStack> Worn, string? DeathItem);

public static class NpcLootPlan
{
    public const string DefaultOutfitLink = "DOFT";
    public const string DeathItemLink = "INAM";
    public const string OutfitItemsLink = "INAM";

    // Constraint: the death item is the NPC_'s own INAM only; no source ties it to a template slot (UNVERIFIED).
    // Requires: inventorySource returns the NPC_ whose own data applies to the Inventory template slot, or null when the chain stopped; null runs mean the loot is undecided.
    public static NpcLootRuns? Plan(
        string npc, IRecordReader reader, Func<string, string?> inventorySource, Func<string, IReadOnlyList<InventoryStack>?> ownRows)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(inventorySource);
        ArgumentNullException.ThrowIfNull(ownRows);
        if (inventorySource(npc) is not { } source || ownRows(source) is not { } carried) return null;
        return new NpcLootRuns(carried, Outfit(reader, source), First(reader, npc, DeathItemLink));
    }

    // Constraint: the default outfit belongs to the Inventory template source; only an OTFT record counts and each of its items is one worn piece.
    internal static IReadOnlyList<InventoryStack> Outfit(IRecordReader reader, string source)
    {
        if (First(reader, source, DefaultOutfitLink) is not { } outfit || reader.Signature(outfit) != "OTFT") return [];
        return reader.FormLinks(outfit, OutfitItemsLink).Take(RadicleContainerRecords.MaxItemRuns).Select(item => new InventoryStack(item, 1)).ToList();
    }

    private static string? First(IRecordReader reader, string id, string subrecord) => reader.FormLinks(id, subrecord) is { Count: > 0 } links ? links[0] : null;

    public static IReadOnlyList<InventoryStack> Merge(params IEnumerable<InventoryStack>[] parts) =>
        ContainerContents.Expand(parts.SelectMany(part => part), string.Empty, _ => false, (_, _, _) => []);
}
