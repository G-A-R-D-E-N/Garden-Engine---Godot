using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workbench;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PickpocketPerkTests
{
    const string Caps = "00000f:fo4.esm";
    const string Gun = "000100:a.esm";
    const byte SetValue = 1;
    const byte CanPickpocketEquippedItem = 0x3D;

    static readonly FormKey Pickpocket01 = new("a.esm", 0xF01);
    static readonly FormKey Pickpocket02 = new("a.esm", 0xF02);
    static readonly FormKey Pickpocket04 = new("a.esm", 0xF04);

    static readonly PickpocketSettings Esm = new(
        ActorSkillBase: 20, ActorSkillMult: 1, TargetSkillBase: 20, TargetSkillMult: -0.25f,
        AmountBase: 0, AmountMult: -0.1f, WeightBase: 0, WeightMult: -4, MinChance: 0, MaxChance: 90, PlantChanceMult: 1);

    static readonly PickpocketTarget Sleeper = new(0, 0, true, false, false, false, false);

    sealed class Source : IBarterPerkSource
    {
        public List<HeldPerk> Player { get; } = new();
        public List<HeldPerk> Vendor { get; } = new();
        public Dictionary<FormKey, PerkEntryPointsRecord> Perks { get; } = new();

        public IReadOnlyList<HeldPerk> Held(BarterActor actor) => actor == BarterActor.Player ? Player : Vendor;
        public PerkEntryPointsRecord? EntryPoints(FormKey perk) => Perks.GetValueOrDefault(perk);
        public IRecipeFacts? Facts(BarterActor actor) => null;
    }

    sealed class Items(int value) : IItemRecords
    {
        public ItemRecord? Find(string id) => new(id, "MISC", "Item " + id, value, 1.0);
    }

    sealed class Actors : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => new("Vic", false, [new(Gun, 3)]);
    }

    static PerkEntryPointEffect Row(byte entry, byte function, float value, byte rank = 0) =>
        new(rank, 0, entry, function, (byte)1, value, Array.Empty<PerkEntryPointTab>());

    static Source Hold(Source source, FormKey perk, int rank, params PerkEntryPointEffect[] effects)
    {
        source.Player.Add(new HeldPerk(perk, rank));
        source.Perks[perk] = new PerkEntryPointsRecord(perk, effects);
        return source;
    }

    static int Chance(Func<float, float>? perk) => PickpocketFormula.Chance(Esm, 5, 5, 10, 1, false, perk);

    [Fact]
    public void Without_a_perk_there_is_no_modifier()
    {
        Assert.Null(PickpocketPerks.Modifier(null));
        Assert.Null(PickpocketPerks.Modifier(new Source()));
        Assert.Equal(39, Chance(PickpocketPerks.Modifier(null)));
    }

    [Fact]
    public void Add_value_lifts_the_hand_computed_chance_by_its_value_before_the_clamp()
    {
        var source = Hold(new Source(), Pickpocket01, 1, Row(PickpocketPerks.ModPickpocketChance, PickpocketPerks.AddValue, 25));

        Assert.Equal(64, Chance(PickpocketPerks.Modifier(source)));
    }

    [Fact]
    public void Several_held_perks_take_the_largest_value_not_the_sum()
    {
        var source = Hold(new Source(), Pickpocket01, 1, Row(PickpocketPerks.ModPickpocketChance, PickpocketPerks.AddValue, 25));
        Hold(source, Pickpocket02, 1, Row(PickpocketPerks.ModPickpocketChance, PickpocketPerks.AddValue, 50));

        Assert.Equal(89, Chance(PickpocketPerks.Modifier(source)));
    }

    [Fact]
    public void Other_functions_and_entry_points_are_ignored()
    {
        Assert.Null(PickpocketPerks.Modifier(Hold(new Source(), Pickpocket01, 1, Row(PickpocketPerks.ModPickpocketChance, SetValue, 25))));
        Assert.Null(PickpocketPerks.Modifier(Hold(new Source(), Pickpocket01, 1, Row(PickpocketPerks.ModPickpocketChance, BarterPerks.MultiplyValue, 25))));
        Assert.Null(PickpocketPerks.Modifier(Hold(new Source(), Pickpocket01, 1, Row(CanPickpocketEquippedItem, PickpocketPerks.AddValue, 25))));
    }

    [Fact]
    public void The_perk_cannot_lift_the_chance_past_the_maximum()
    {
        var perk = PickpocketPerks.Modifier(Hold(new Source(), Pickpocket04, 1, Row(PickpocketPerks.ModPickpocketChance, PickpocketPerks.AddValue, 100)));
        var pick = new PickpocketContext(Esm, 5, 5, new Items(10), Sleeper, true, Perk: perk);

        var inventory = ActorInventory.OpenPickpocket("npc1", "base", new Actors(), new ContainerLedger(), new PlayerItems(), Caps, _ => 1, pick, out var refusal);

        Assert.Equal(string.Empty, refusal);
        Assert.Equal(90, inventory!.Chance(ContainerSide.Container, Gun));
    }
}
