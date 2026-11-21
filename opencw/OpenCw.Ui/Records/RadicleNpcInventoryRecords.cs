using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using LevelReason = Radicle.Interop.Native.LevelReason;
using NpcTemplateReason = Radicle.Interop.Native.NpcTemplateReason;
using NpcTemplateSlot = Radicle.Interop.Native.NpcTemplateSlot;

namespace OpenCommonwealth.Services.Ui.Records;

// Constraint: a living actor's items are the Inventory-template source's CNTO; a dead one (dead set) adds its default outfit and the rolled death item as DeathStacks, and the actor is never reported alive here.
public sealed class RadicleNpcInventoryRecords : IActorRecords
{
    public const string DeathSeedSuffix = "|death";

    private readonly IRecordSource _source;
    private readonly IRecordReader _reader;
    private readonly RadicleContainerRecords _containers;
    private readonly uint _playerLevel;
    private readonly bool _dead;

    public RadicleNpcInventoryRecords(IRecordSource source, uint playerLevel, bool dead = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _reader = new RadicleRecordReader(source);
        _playerLevel = Math.Max(1u, playerLevel);
        _dead = dead;
        _containers = new RadicleContainerRecords(source, playerLevel);
    }

    public RadicleNpcInventoryRecords(RadicleEnvironment env, StringTables strings, uint playerLevel, bool dead = false)
        : this(new PairRecordSource(env, strings), playerLevel, dead)
    {
    }

    private RadicleEnvironment _env => _source.Environment;

    private StringTables _strings => _source.Strings;

    public ActorSetup? Read(string refKey, string baseKey)
    {
        try
        {
            if (!RecordKey.TryParse(baseKey, out var plugin, out var local) || _env.Winner(new FormKey(plugin, local)) is not { } npc) return null;
            var info = _env.Info(npc);
            if (info.Signature != "NPC_") return null;
            var name = _strings.ResolveName(npc) is { } bytes ? RadicleRecordReader.DecodeName(bytes) : info.NameText;
            var title = string.IsNullOrWhiteSpace(name) ? info.EditorId ?? baseKey : name;
            NpcLootRuns? plan;
            try { plan = NpcLootPlan.Plan(baseKey, _reader, InventorySource, OwnRows); }
            catch (Exception e) when (e is RadicleException or InvalidOperationException) { plan = null; }
            if (plan is null) return new ActorSetup(title, false, Array.Empty<InventoryStack>(), false);
            try
            {
                var carried = _containers.Expand(plan.Carried, refKey);
                if (!_dead) return new ActorSetup(title, false, carried);
                var worn = _containers.Expand(plan.Worn, refKey);
                var death = plan.DeathItem is { } list ? new RadicleContainerRecords(_source, DeathRollLevel(npc)).Expand([new InventoryStack(list, 1)], refKey + DeathSeedSuffix) : Array.Empty<InventoryStack>();
                return new ActorSetup(title, false, carried, true, NpcLootPlan.Merge(worn, death));
            }
            catch (InvalidOperationException) { return new ActorSetup(title, false, Array.Empty<InventoryStack>(), false); }
        }
        catch (RadicleException)
        {
            return null;
        }
    }

    // Requires: a source NPC_ reachable by the Inventory template walk; NoLink stays on the last NPC_ (UNVERIFIED), any other stop reason is undecided.
    private string? InventorySource(string npcKey)
    {
        if (!RecordKey.TryParse(npcKey, out var plugin, out var local) || _env.Winner(new FormKey(plugin, local)) is not { } record) return null;
        var source = _env.NpcTemplateSource(record, NpcTemplateSlot.Inventory);
        return ChooseSource(npcKey, source.Reason, RecordKey.Format(source.Source));
    }

    // Constraint: a template chain that ends at a leveled list or a non-NPC_ link falls back to the NPC_'s own rows (the pre-template behaviour, UNVERIFIED); other stops leave the loot undecided.
    internal static string? ChooseSource(string npcKey, NpcTemplateReason reason, string? sourceKey) => reason switch
    {
        NpcTemplateReason.Own or NpcTemplateReason.TemplateNpc or NpcTemplateReason.NoLink => sourceKey,
        NpcTemplateReason.LeveledList or NpcTemplateReason.NotNpc => npcKey,
        _ => null,
    };

    private IReadOnlyList<InventoryStack>? OwnRows(string npcKey) =>
        RecordRows.NpcItems(_reader, npcKey) is { } rows ? Runs(rows) : null;

    private uint DeathRollLevel(Record npc)
    {
        try
        {
            var level = _env.ActorLevelForSpawn(npc, _playerLevel, 0);
            return DeathRollLevel(level.Level, level.Reason, _playerLevel);
        }
        catch (RadicleException) { return _playerLevel; }
    }

    internal static uint DeathRollLevel(uint level, LevelReason reason, uint playerLevel) =>
        reason == LevelReason.Fixed && level > 0 ? level : Math.Max(1u, playerLevel);

    // Constraint: a list above MaxItemRuns throws, which Read reports as an incomplete inventory instead of freezing a cut one.
    internal static List<InventoryStack> Runs(IEnumerable<InventoryStack> rows)
    {
        var runs = rows.Where(row => row.Count > 0).Take(RadicleContainerRecords.MaxItemRuns + 1).ToList();
        if (runs.Count > RadicleContainerRecords.MaxItemRuns) throw new InvalidOperationException($"item list exceeds {RadicleContainerRecords.MaxItemRuns} rows");
        return runs;
    }
}
