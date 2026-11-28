using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlayerStatusFactsTests
{
    sealed class Items : IItemRecords
    {
        public Dictionary<string, ItemRecord> Records { get; } = new();

        public ItemRecord? Find(string id) => Records.GetValueOrDefault(id);
    }

    static Items Known()
    {
        var items = new Items();
        items.Records["gun"] = new ItemRecord("gun", "WEAP", "Pipe Gun", 30, 5.5);
        items.Records["vest"] = new ItemRecord("vest", "ARMO", "Leather Vest", 20, 8);
        items.Records["can"] = new ItemRecord("can", "MISC", "Tin Can", 1, 0.25);
        return items;
    }

    static string? Value(IReadOnlyList<StatusRow> rows, string key) => rows.FirstOrDefault(row => row.Key == key)?.Value;

    [Fact]
    public void Action_points_equipment_and_carry_weight_come_from_the_live_stores()
    {
        var equipment = new PlayerEquipment();
        equipment.SetWeapon("gun", ItemState.Plain);
        equipment.Wear("vest", ItemState.Plain, 1);
        var stacks = new[] { new InventoryStack("gun", 1), new InventoryStack("vest", 1), new InventoryStack("can", 4) };

        var rows = PlayerStatusFacts.Build(stacks, Known(), equipment, 70.9, 100);

        Assert.Equal("70/100", Value(rows, PlayerStatusFacts.ActionPointsKey));
        Assert.Equal("Pipe Gun", Value(rows, PlayerStatusFacts.WeaponKey));
        Assert.Equal("Leather Vest", Value(rows, PlayerStatusFacts.ArmorKeyPrefix + "vest"));
        Assert.Equal("14.5", Value(rows, PlayerStatusFacts.WeightKey));
    }

    [Fact]
    public void Unknown_stats_are_omitted_never_invented()
    {
        var rows = PlayerStatusFacts.Build(new[] { new InventoryStack("mystery", 2) }, Known(), null, null, null);

        Assert.Empty(rows);
    }

    [Fact]
    public void Unarmed_and_unworn_show_no_equipment_rows_and_an_empty_pack_weighs_nothing()
    {
        var rows = PlayerStatusFacts.Build(System.Array.Empty<InventoryStack>(), Known(), new PlayerEquipment(), double.NaN, 100);

        Assert.Equal(new[] { PlayerStatusFacts.WeightKey }, rows.Select(row => row.Key));
        Assert.Equal("0", rows[0].Value);
    }

    [Fact]
    public void Action_points_clamp_to_the_maximum_and_a_zero_maximum_is_dropped()
    {
        Assert.Equal("100/100", Value(PlayerStatusFacts.Build([], Known(), null, 250, 100), PlayerStatusFacts.ActionPointsKey));
        Assert.Null(Value(PlayerStatusFacts.Build([], Known(), null, 0, 0), PlayerStatusFacts.ActionPointsKey));
    }

    [Fact]
    public void An_unnamed_item_falls_back_to_its_id_and_stacks_without_a_weight_drop_the_weight_row()
    {
        var items = Known();
        items.Records["odd"] = new ItemRecord("odd", "MISC", "", null, null);
        var equipment = new PlayerEquipment();
        equipment.SetWeapon("odd", ItemState.Plain);

        var rows = PlayerStatusFacts.Build(new[] { new InventoryStack("odd", 1) }, items, equipment, null, null);

        Assert.Equal("odd", Value(rows, PlayerStatusFacts.WeaponKey));
        Assert.Null(Value(rows, PlayerStatusFacts.WeightKey));
    }

    [Fact]
    public void The_live_model_lists_the_facts_and_follows_the_stores_on_refresh()
    {
        var equipment = new PlayerEquipment();
        var items = Known();
        var model = new PlayerStatusModel(new LivePlayerStatus(), () => PlayerStatusFacts.Build([], items, equipment, 40, 90), true);

        Assert.Equal(new[] { "STATUS" }, model.SubTabLabels);
        Assert.Equal(new[] { PlayerStatusFacts.ActionPointsKey, PlayerStatusFacts.WeightKey }, model.View.Visible.Select(row => row.Key));
        Assert.Empty(model.Condition);
        Assert.Null(model.RadiationFraction);
        Assert.Empty(model.Aid);

        equipment.SetWeapon("gun", ItemState.Plain);
        model.Refresh();

        Assert.Contains(model.View.Visible, row => row.Key == PlayerStatusFacts.WeaponKey && row.Value == "Pipe Gun");
        Assert.Equal(PipBoyTab.Stats, model.Tab);
    }

    [Fact]
    public void A_status_only_model_has_no_special_or_perks_to_open()
    {
        var model = new PlayerStatusModel(new LivePlayerStatus(), () => [], true);

        model.SetSubTab(2);

        Assert.Equal(StatusSubTab.Status, model.SubTab);
        Assert.Empty(model.Facts);
    }
}
