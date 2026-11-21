using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;
using LevelReason = Radicle.Interop.Native.LevelReason;
using NpcTemplateReason = Radicle.Interop.Native.NpcTemplateReason;

namespace OpenCw.Ui.Tests;

public class NpcLootPlanTests
{
    const string Raider = "000010:Fallout4.esm";
    const string Template = "000011:Fallout4.esm";
    const string Outfit = "000020:Fallout4.esm";
    const string Armor = "000021:Fallout4.esm";
    const string Hat = "000022:Fallout4.esm";
    const string Death = "000030:Fallout4.esm";
    const string TemplateDeath = "000031:Fallout4.esm";
    const string Gun = "000040:Fallout4.esm";
    const string Ammo = "000041:Fallout4.esm";

    static FakeRecordReader Reader() => new();

    static FakeRecord Npc(FakeRecordReader reader, string id, string? outfit = null, string? death = null)
    {
        var record = reader.Add(id, "NPC_");
        if (outfit is not null) record.Links["DOFT"] = [outfit];
        if (death is not null) record.Links["INAM"] = [death];
        return record;
    }

    static void AddOutfit(FakeRecordReader reader, string id, params string[] items) => reader.Add(id, "OTFT").Links["INAM"] = items;

    static readonly InventoryStack[] None = [];

    static string? Own(string id) => id;

    static InventoryStack Stack(string id, int count) => new(id, count);

    [Fact]
    public void An_npc_that_keeps_its_own_inventory_carries_its_rows_its_outfit_and_its_death_item()
    {
        var reader = Reader();
        Npc(reader, Raider, Outfit, Death);
        AddOutfit(reader, Outfit, Armor, Hat);

        var plan = NpcLootPlan.Plan(Raider, reader, Own, id => id == Raider ? new[] { Stack(Gun, 1), Stack(Ammo, 12) } : null);

        Assert.NotNull(plan);
        Assert.Equal(new[] { Stack(Gun, 1), Stack(Ammo, 12) }, plan!.Carried);
        Assert.Equal(new[] { Stack(Armor, 1), Stack(Hat, 1) }, plan.Worn);
        Assert.Equal(Death, plan.DeathItem);
    }

    [Fact]
    public void A_templated_inventory_comes_from_the_template_source_with_its_outfit()
    {
        var reader = Reader();
        Npc(reader, Raider, outfit: null);
        Npc(reader, Template, Outfit);
        AddOutfit(reader, Outfit, Armor);

        var plan = NpcLootPlan.Plan(Raider, reader, id => id == Raider ? Template : null, id => id == Template ? new[] { Stack(Gun, 1) } : new[] { Stack(Ammo, 99) });

        Assert.Equal(new[] { Stack(Gun, 1) }, plan!.Carried);
        Assert.Equal(new[] { Stack(Armor, 1) }, plan.Worn);
    }

    [Fact]
    public void The_death_item_is_the_npcs_own_and_is_not_taken_from_the_template_source()
    {
        var reader = Reader();
        Npc(reader, Raider, death: Death);
        Npc(reader, Template, death: TemplateDeath);

        Assert.Equal(Death, NpcLootPlan.Plan(Raider, reader, _ => Template, _ => None)!.DeathItem);

        reader.Records[Raider].Links.Remove("INAM");
        Assert.Null(NpcLootPlan.Plan(Raider, reader, _ => Template, _ => None)!.DeathItem);
    }

    [Fact]
    public void A_stopped_template_chain_or_an_unreadable_source_leaves_the_loot_undecided()
    {
        var reader = Reader();
        Npc(reader, Raider);

        Assert.Null(NpcLootPlan.Plan(Raider, reader, _ => null, _ => None));
        Assert.Null(NpcLootPlan.Plan(Raider, reader, Own, _ => null));
    }

    [Fact]
    public void A_default_outfit_that_is_not_an_otft_record_adds_nothing()
    {
        var reader = Reader();
        Npc(reader, Raider, Armor);
        reader.Add(Armor, "ARMO");

        Assert.Empty(NpcLootPlan.Plan(Raider, reader, Own, _ => None)!.Worn);

        Npc(reader, Template, "0000FF:Fallout4.esm");
        Assert.Empty(NpcLootPlan.Plan(Template, reader, Own, _ => None)!.Worn);
    }

    [Fact]
    public void Merge_sums_a_repeated_item_across_the_parts_in_first_seen_order()
    {
        var merged = NpcLootPlan.Merge(new[] { Stack(Gun, 1), Stack(Ammo, 5) }, new[] { Stack(Ammo, 7), Stack(Armor, 1) }, new[] { Stack(Hat, 0) });

        Assert.Equal(new[] { Stack(Gun, 1), Stack(Ammo, 12), Stack(Armor, 1) }, merged);
    }

    [Theory]
    [InlineData(NpcTemplateReason.Own, Template)]
    [InlineData(NpcTemplateReason.TemplateNpc, Template)]
    [InlineData(NpcTemplateReason.NoLink, Template)]
    [InlineData(NpcTemplateReason.LeveledList, Raider)]
    [InlineData(NpcTemplateReason.NotNpc, Raider)]
    [InlineData(NpcTemplateReason.TooDeep, null)]
    [InlineData(NpcTemplateReason.NotFound, null)]
    [InlineData(NpcTemplateReason.Undecodable, null)]
    public void A_template_chain_ending_at_a_leveled_list_uses_the_npcs_own_rows_and_other_stops_are_undecided(NpcTemplateReason reason, string? expected)
    {
        Assert.Equal(expected, RadicleNpcInventoryRecords.ChooseSource(Raider, reason, Template));
    }

    [Theory]
    [InlineData(12u, LevelReason.Fixed, 5u, 12u)]
    [InlineData(0u, LevelReason.Fixed, 5u, 5u)]
    [InlineData(12u, LevelReason.TemplateStatsUnproven, 5u, 5u)]
    [InlineData(12u, LevelReason.LevelMultUnproven, 0u, 1u)]
    public void The_death_roll_uses_the_proven_actor_level_and_otherwise_the_player_level(uint level, LevelReason reason, uint player, uint expected)
    {
        Assert.Equal(expected, RadicleNpcInventoryRecords.DeathRollLevel(level, reason, player));
    }
}
