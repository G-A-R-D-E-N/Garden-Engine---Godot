using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlayerStatusGap9Tests
{
    sealed class Live : IPlayerStatusSource
    {
        public string? PlayerName => "Nora";

        public double? Health { get; set; } = 40;

        public double? MaxHealth { get; set; } = 100;

        public double? PendingHealthGain { get; set; } = 25;

        public double? Radiation { get; set; } = 0.25;

        public IReadOnlyList<DamageTotal>? DamageTotals { get; set; } = [new DamageTotal("Physical", 10)];

        public IReadOnlyList<DamageTotal>? ResistTotals { get; set; } = [new DamageTotal("energy", 5.5), new DamageTotal("fire", 0)];

        public AidSupply? Stimpak { get; set; } = new("Stimpak", 3);

        public AidSupply? RadAway { get; set; } = new("RadAway", 0);

        public List<EffectSource> EffectList { get; } = [];

        public List<SpecialEntry> SpecialList { get; } = [];

        public List<OwnedPerk> PerkList { get; } = [];

        public IReadOnlyList<EffectSource> Effects => EffectList;

        public IReadOnlyList<SpecialEntry> Special => SpecialList;

        public IReadOnlyList<OwnedPerk> Perks => PerkList;

        public bool ReadOnly { get; set; }

        public LimbCondition? Limb(LimbKind limb) => limb switch
        {
            LimbKind.Head => new LimbCondition(0.5),
            LimbKind.RightArm => new LimbCondition(0, true),
            LimbKind.LeftLeg => new LimbCondition(0),
            _ => null,
        };
    }

    static FakeRecordReader Data()
    {
        var reader = new FakeRecordReader();
        reader.Add("000001:a.esm", "AVIF", "Strength", "Strength");
        reader.Add("000004:a.esm", "AVIF", "Perception", "Perception");
        RecordPlayerStatusTests.PerkOf(reader.Add("000002:a.esm", "PERK", "Gunslinger01", "Gunslinger"), "More damage.", null);
        return reader;
    }

    static PlayerStatusModel Model(Live live) => new(new RecordPlayerStatusSource(live, Data()));

    static string[] Flatten(IEnumerable<EffectGroupView> view) =>
        view.SelectMany(group => group.Entries.Select(entry => group.Group + "/" + entry.Source + ": " + string.Join(", ", entry.Lines))).ToArray();

    [Fact]
    public void Condition_rows_list_each_known_limb_in_order_then_the_torso_with_the_pending_heal()
    {
        var condition = Model(new Live()).Condition;

        Assert.Equal(
            new[]
            {
                new ConditionRow("limb:head", "Head", 0.5, 0.5, "50%", false),
                new ConditionRow("limb:rightarm", "Right arm", 0, 0, "CRIPPLED", true),
                new ConditionRow("limb:leftleg", "Left leg", 0, 0, "0%", false),
                new ConditionRow("limb:torso", "Torso", 0.4, 0.65, "40/100", false),
            },
            condition);
    }

    [Fact]
    public void Aid_actions_are_labelled_with_the_supply_and_enabled_only_with_stock_on_a_live_game()
    {
        var live = new Live();
        Assert.Equal(
            new[]
            {
                new AidAction(AidKind.Stimpak, "USE STIMPAK (3)", true),
                new AidAction(AidKind.RadAway, "USE RADAWAY (0)", false),
            },
            Model(live).Aid);

        live.ReadOnly = true;
        Assert.Equal(
            new[]
            {
                new AidAction(AidKind.Stimpak, "USE STIMPAK (3)", false),
                new AidAction(AidKind.RadAway, "USE RADAWAY (0)", false),
            },
            Model(live).Aid);

        live.Stimpak = null;
        Assert.Equal(new[] { new AidAction(AidKind.RadAway, "USE RADAWAY (0)", false) }, Model(live).Aid);
    }

    [Fact]
    public void The_effect_view_groups_by_group_number_merges_repeated_lines_and_drops_empty_entries()
    {
        var live = new Live();
        live.EffectList.Add(new EffectSource(2, "Mentats", [new EffectLine("Perception", 2), new EffectLine("Intelligence", 2)]));
        live.EffectList.Add(new EffectSource(1, "Psycho",
            [new EffectLine("Damage", 25, true), new EffectLine("Radiation", -1), new EffectLine("Damage", 5, true, 30)]));
        live.EffectList.Add(new EffectSource(1, "Empty", []));
        live.EffectList.Add(new EffectSource(3, "Silent", []));
        live.EffectList.Add(new EffectSource(1, "Rad-X", [new EffectLine("Rad Resist", 0, false, 0, true)]));

        var view = Model(live).EffectView;

        Assert.Equal(new[] { 1, 2 }, view.Select(group => group.Group).ToArray());
        Assert.Equal(
            new[]
            {
                "1/PSYCHO: DAMAGE +30%, RADIATION -1",
                "1/RAD-X: RAD RESIST",
                "2/MENTATS: PERCEPTION +2, INTELLIGENCE +2",
            },
            Flatten(view));
        Assert.Equal(
            new[] { "PSYCHO:", "DAMAGE +30%", "RADIATION -1", "RAD-X:", "RAD RESIST", "MENTATS:", "PERCEPTION +2", "INTELLIGENCE +2" },
            Model(live).EffectLines.ToArray());
    }

    [Fact]
    public void The_status_tab_rows_show_condition_radiation_damage_resistance_aid_and_the_effects_toggle()
    {
        var live = new Live();
        live.EffectList.Add(new EffectSource(1, "Mentats", [new EffectLine("Perception", 2)]));
        var model = Model(live);

        Assert.Equal(
            new[]
            {
                new StatusRow("limb:head", "Head", "50%"),
                new StatusRow("limb:rightarm", "Right arm", "CRIPPLED"),
                new StatusRow("limb:leftleg", "Left leg", "0%"),
                new StatusRow("limb:torso", "Torso", "40/100"),
                new StatusRow("radiation", "Radiation", "25%"),
                new StatusRow("damage", "Damage", "PHYSICAL 10"),
                new StatusRow("resist", "Resistance", "ENERGY 5.5"),
                new StatusRow("aid:stimpak", "USE STIMPAK (3)", ""),
                new StatusRow("aid:radaway", "USE RADAWAY (0)", ""),
                new StatusRow("effects", "Show effects", ""),
            },
            model.View.Visible.ToArray());

        Assert.True(model.ToggleEffects());
        Assert.Equal(new StatusRow("effects", "Hide effects", ""), model.View.Visible.Last());
    }

    [Fact]
    public void The_special_and_perk_rows_use_the_names_the_record_source_resolved()
    {
        var live = new Live();
        live.SpecialList.Add(new SpecialEntry("Strength", "STR", 6, 1));
        live.SpecialList.Add(new SpecialEntry("000004:a.esm", "x", 5.5, -2));
        live.SpecialList.Add(new SpecialEntry("Luck", "Luck", 4));
        live.PerkList.Add(new OwnedPerk("Zed", "Zed", 1, []));
        live.PerkList.Add(new OwnedPerk("Gunslinger01", "old", 2, ["fallback"]));
        var model = Model(live);

        model.SetSubTab(StatusSubTab.Special);
        Assert.Equal(
            new[]
            {
                new StatusRow("special:Strength", "Strength", "+6"),
                new StatusRow("special:000004:a.esm", "Perception", "-5.5"),
                new StatusRow("special:Luck", "Luck", "4"),
            },
            model.View.Visible.ToArray());

        model.SetSubTab(StatusSubTab.Perks);
        Assert.Equal(
            new[]
            {
                new StatusRow("perk:Gunslinger01", "Gunslinger (2)", ""),
                new StatusRow("perk:Zed", "Zed", ""),
            },
            model.View.Visible.ToArray());
    }
}
