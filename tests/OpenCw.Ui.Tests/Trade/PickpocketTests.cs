using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PickpocketTests
{
    const string Caps = "00000f:fo4.esm";
    const string Gun = "000100:a.esm";

    static readonly PickpocketSettings Esm = new(
        ActorSkillBase: 20, ActorSkillMult: 1, TargetSkillBase: 20, TargetSkillMult: -0.25f,
        AmountBase: 0, AmountMult: -0.1f, WeightBase: 0, WeightMult: -4, MinChance: 0, MaxChance: 90, PlantChanceMult: 1);

    static readonly PickpocketTarget Sleeper = new(0, 0, true, false, false, false, false);

    sealed class Items(int value, double weight) : IItemRecords
    {
        public ItemRecord? Find(string id) => new(id, "MISC", "Item " + id, value, weight);
    }

    sealed class Actors(bool complete = true) : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => new("Vic", false, [new(Gun, 3), new(Caps, 5)], complete);
    }

    static ActorInventory Open(PickpocketTarget target, Func<int> roll, bool sneaking = true, PlayerItems? player = null, ContainerLedger? ledger = null, Func<float, float>? perk = null)
    {
        var pick = new PickpocketContext(Esm, 5, 5, new Items(10, 1), target, sneaking, roll, perk);
        var inventory = ActorInventory.OpenPickpocket("npc1", "base", new Actors(), ledger ?? new ContainerLedger(), player ?? new PlayerItems(), Caps, _ => 1, pick, out var refusal);
        Assert.Equal(string.Empty, refusal);
        return inventory!;
    }

    [Fact]
    public void Vanilla_settings_give_the_hand_computed_chance()
    {
        Assert.Equal(39, PickpocketFormula.Chance(Esm, 5, 5, 10, 1, false));
    }

    [Fact]
    public void Exe_defaults_fill_every_setting()
    {
        Assert.Equal(47, PickpocketFormula.Chance(new PickpocketSettings(), 5, 5, 10, 1, false));
    }

    [Theory]
    [InlineData(6, 44)]
    [InlineData(7, 43)]
    public void The_fraction_rounds_up_from_one_half(float perception, int expected)
    {
        Assert.Equal(expected, PickpocketFormula.Chance(Esm, 5, perception, 0, 0, false));
    }

    [Fact]
    public void Chance_clamps_to_the_maximum_before_rounding()
    {
        Assert.Equal(90, PickpocketFormula.Chance(Esm with { MaxChance = 90.4f }, 5, 5, 0, 0, false, x => x * 3));
        Assert.Equal(90, PickpocketFormula.Chance(Esm, 5, 5, 0, 0, false, x => x * 3));
    }

    [Fact]
    public void Chance_clamps_to_the_minimum()
    {
        Assert.Equal(0, PickpocketFormula.Chance(Esm, 5, 5, 10, 100, false));
        Assert.Equal(5, PickpocketFormula.Chance(new PickpocketSettings(), 5, 5, 10, 100, false));
    }

    [Fact]
    public void Placing_scales_the_chance_after_the_perk_entry()
    {
        var plant = Esm with { PlantChanceMult = 0.5f };
        Assert.Equal(44, PickpocketFormula.Chance(plant, 5, 6, 0, 0, false));
        Assert.Equal(22, PickpocketFormula.Chance(plant, 5, 6, 0, 0, true));
        Assert.Equal(27, PickpocketFormula.Chance(plant, 5, 6, 0, 0, true, x => x + 10));
    }

    [Fact]
    public void Stolen_value_has_a_floor_of_one_and_weight_scales_with_count()
    {
        Assert.Equal(1, PickpocketFormula.StolenValue(0, 5));
        Assert.Equal(12, PickpocketFormula.StolenValue(3, 4));
        Assert.Equal(1, PickpocketFormula.StolenValue(3, 0));
        Assert.Equal(2.5f, PickpocketFormula.StolenWeight(0.5, 5));
    }

    [Fact]
    public void A_roll_below_the_chance_succeeds()
    {
        Assert.True(PickpocketFormula.Succeeds(39, 38));
        Assert.False(PickpocketFormula.Succeeds(39, 39));
        Assert.False(PickpocketFormula.Succeeds(0, 0));
        Assert.True(PickpocketFormula.Succeeds(100, PickpocketFormula.RollCount - 1));
    }

    [Fact]
    public void Read_takes_values_from_the_GMST_records_and_defaults_the_rest()
    {
        var reader = new FakeRecordReader();
        Gmst(reader, "000001:fo4.esm", "fPickPocketActorSkillBase", "20");
        Gmst(reader, "000002:fo4.esm", "fPickPocketAmountMult", "-0.1");
        Gmst(reader, "000003:fo4.esm", "fPickPocketMaxChance", "not a number");

        var settings = PickpocketSettings.Read(reader);

        Assert.Equal(20f, settings.ActorSkillBase);
        Assert.Equal(-0.1f, settings.AmountMult);
        Assert.Equal(95f, settings.MaxChance);
        Assert.Equal(new PickpocketSettings() with { ActorSkillBase = 20, AmountMult = -0.1f }, settings);
    }

    static void Gmst(FakeRecordReader reader, string id, string editorId, string value) =>
        reader.Add(id, "GMST", editorId).Scalars[PickpocketSettings.FloatPath] = value;

    [Theory]
    [InlineData(true, 0, 0, true, false, false, false, false, PickpocketOffer.Open)]
    [InlineData(false, 0, 0, true, false, false, false, false, PickpocketOffer.NotOffered)]
    [InlineData(true, 2, 0, true, false, false, false, false, PickpocketOffer.NotOffered)]
    [InlineData(true, 0, 0, false, false, false, false, false, PickpocketOffer.NotOffered)]
    [InlineData(true, 0, 0, true, true, false, false, false, PickpocketOffer.NotOffered)]
    [InlineData(true, 0, 0, true, false, false, false, true, PickpocketOffer.NotOffered)]
    [InlineData(true, 0, 0, true, false, true, false, false, PickpocketOffer.AlreadyCaught)]
    [InlineData(true, 0, 0, true, false, false, true, false, PickpocketOffer.AlreadyCaught)]
    [InlineData(true, 0, 3, true, false, false, true, false, PickpocketOffer.Open)]
    [InlineData(true, 0, 7, true, false, false, true, false, PickpocketOffer.AlreadyCaught)]
    public void Offer_follows_the_activation_predicates(
        bool sneaking, int life, int knock, bool race, bool alias, bool angry, bool caught, bool mount, PickpocketOffer expected)
    {
        Assert.Equal(expected, PickpocketRules.Offer(new PickpocketTarget(life, knock, race, alias, angry, caught, mount), sneaking));
    }

    [Fact]
    public void A_ghost_is_not_offered_the_pickpocket_route()
    {
        Assert.Equal(PickpocketOffer.NotOffered, PickpocketRules.Offer(new PickpocketTarget(0, 0, true, false, false, false, false, IsGhost: true), true));
        Assert.Equal(PickpocketOffer.Open, PickpocketRules.Offer(new PickpocketTarget(0, 0, true, false, false, false, false, IsGhost: false), true));
    }

    [Fact]
    public void A_player_teammate_is_not_offered_the_pickpocket_route()
    {
        Assert.Equal(PickpocketOffer.NotOffered, PickpocketRules.Offer(new PickpocketTarget(0, 0, true, false, false, false, false, IsPlayerTeammate: true), true));
    }

    [Fact]
    public void Open_refuses_when_the_offer_is_withheld()
    {
        var pick = new PickpocketContext(Esm, 5, 5, new Items(10, 1), Sleeper, false);
        var inventory = ActorInventory.OpenPickpocket("npc1", "base", new Actors(), new ContainerLedger(), new PlayerItems(), Caps, _ => 1, pick, out var refusal);

        Assert.Null(inventory);
        Assert.Equal(ActorInventory.NotOfferedText, refusal);
    }

    [Fact]
    public void Open_names_the_actor_that_already_caught_the_player()
    {
        var pick = new PickpocketContext(Esm, 5, 5, new Items(10, 1), Sleeper with { AlreadyCaught = true }, true);
        var inventory = ActorInventory.OpenPickpocket("npc1", "base", new Actors(), new ContainerLedger(), new PlayerItems(), Caps, _ => 1, pick, out var refusal);

        Assert.Null(inventory);
        Assert.Equal("Vic has already caught you.", refusal);
    }

    [Fact]
    public void Pickpocket_mode_is_reported_and_the_card_chance_uses_the_formula()
    {
        var inventory = Open(Sleeper, () => 0);

        Assert.Equal(ContainerMode.Pickpocket, inventory.Mode);
        Assert.Equal(39, inventory.Chance(ContainerSide.Container, Gun));
        Assert.Equal(34, inventory.Chance(ContainerSide.Container, Gun, 2));
        Assert.Equal(0, inventory.Chance(ContainerSide.Player, Gun));
    }

    [Fact]
    public void A_successful_roll_moves_the_stolen_items()
    {
        var player = new PlayerItems();
        var inventory = Open(Sleeper, () => 38, player: player);

        var result = inventory.Transfer(ContainerSide.Container, Gun, 1);

        Assert.Equal(TransferStatus.Done, result.Status);
        Assert.Equal(1, player.CountOf(Gun));
        Assert.Equal(2, inventory.ContainerStacks.Single(s => s.Id == Gun).Count);
        Assert.False(inventory.WasCaught);
    }

    [Fact]
    public void A_failed_roll_reports_caught_and_moves_nothing()
    {
        var player = new PlayerItems();
        var inventory = Open(Sleeper, () => 39, player: player);

        var result = inventory.Transfer(ContainerSide.Container, Gun, 1);

        Assert.Equal(TransferStatus.Caught, result.Status);
        Assert.Equal(ActorInventory.CaughtText, result.Reason);
        Assert.Equal(0, player.CountOf(Gun));
        Assert.True(inventory.WasCaught);
    }

    [Fact]
    public void A_knocked_out_target_skips_the_roll()
    {
        var player = new PlayerItems();
        var inventory = Open(Sleeper with { KnockState = 3 }, () => 99, player: player);

        Assert.Equal(TransferStatus.Done, inventory.Transfer(ContainerSide.Container, Gun, 1).Status);
        Assert.Equal(1, player.CountOf(Gun));
    }

    [Fact]
    public void Placing_items_is_refused_with_the_game_text()
    {
        var player = new PlayerItems();
        player.Add(Gun, 1);
        var inventory = Open(Sleeper, () => 0, player: player);

        Assert.Equal(ActorInventory.PlaceText, ((IContainerInventory)inventory).Refusal(ContainerSide.Player, Gun, 1));
        Assert.Equal(TransferStatus.Refused, inventory.Transfer(ContainerSide.Player, Gun, 1).Status);
        Assert.Equal(1, player.CountOf(Gun));
    }

    [Fact]
    public void Taking_more_than_the_actor_holds_is_refused_before_rolling()
    {
        var rolled = false;
        var inventory = Open(Sleeper, () => { rolled = true; return 0; });

        Assert.Equal(TransferStatus.Refused, inventory.Transfer(ContainerSide.Container, Gun, 9).Status);
        Assert.False(rolled);
    }

    [Fact]
    public void Loot_mode_is_unchanged_for_a_dead_actor()
    {
        var inventory = ActorInventory.Open("npc1", "base", new Actors(), new ContainerLedger(), new PlayerItems(), Caps, _ => 1, out _)!;

        Assert.Equal(ContainerMode.Loot, inventory.Mode);
        Assert.Equal(0, inventory.Chance(ContainerSide.Container, Gun, 3));
    }

    static ActorInventory? Reopen(ContainerLedger ledger, PlayerItems player, PickpocketTarget target, out string refusal) =>
        ActorInventory.OpenPickpocket("npc1", "base", new Actors(), ledger, player, Caps, _ => 1,
            new PickpocketContext(Esm, 5, 5, new Items(10, 1), target with { AlreadyCaught = target.AlreadyCaught || ledger.IsCaught("npc1") }, true, () => 0), out refusal);

    [Fact]
    public void A_caught_attempt_is_kept_in_the_ledger_and_the_next_open_names_the_actor()
    {
        var ledger = new ContainerLedger();
        var inventory = Open(Sleeper, () => 39, ledger: ledger);

        Assert.Equal(TransferStatus.Caught, inventory.Transfer(ContainerSide.Container, Gun, 1).Status);

        Assert.True(ledger.IsCaught("npc1"));
        Assert.Null(Reopen(ledger, new PlayerItems(), Sleeper, out var refusal));
        Assert.Equal("Vic has already caught you.", refusal);
        Assert.Equal(new[] { new InventoryStack(Gun, 3), new InventoryStack(Caps, 5) }, ledger.Find("npc1")!.Stacks);
    }

    [Fact]
    public void A_successful_steal_does_not_mark_the_actor_and_a_knocked_out_caught_actor_opens_again()
    {
        var ledger = new ContainerLedger();
        Open(Sleeper, () => 0, ledger: ledger).Transfer(ContainerSide.Container, Gun, 1);
        Assert.False(ledger.IsCaught("npc1"));

        ledger.MarkCaught("npc1");

        Assert.NotNull(Reopen(ledger, new PlayerItems(), Sleeper with { KnockState = 3 }, out var refusal));
        Assert.Equal(string.Empty, refusal);
    }

    [Fact]
    public void The_caught_mark_survives_save_and_load_and_a_new_save_without_it_clears_it()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        Open(Sleeper, () => 39, ledger: ledger, player: player).Transfer(ContainerSide.Container, Gun, 1);

        var sections = ContainerStateSave.Sections(ledger, player);
        var loaded = new ContainerLedger();
        ContainerStateSave.Apply(sections, loaded, new PlayerItems());

        Assert.True(loaded.IsCaught("NPC1"));
        Assert.Equal(new[] { "npc1" }, loaded.Caught);
        Assert.Null(Reopen(loaded, new PlayerItems(), Sleeper, out _));

        ContainerStateSave.Apply(new Dictionary<string, byte[]>(), loaded, new PlayerItems());
        Assert.False(loaded.IsCaught("npc1"));
    }

    [Fact]
    public void A_caught_mark_alone_makes_the_ledger_non_empty_and_a_save_without_the_key_still_loads()
    {
        var ledger = new ContainerLedger();
        ledger.MarkCaught("npc1");
        Assert.False(ledger.IsEmpty);
        Assert.True(ContainerStateSave.Sections(ledger, new PlayerItems()).ContainsKey(ContainerStateSave.SectionName));

        var older = new Dictionary<string, byte[]>
        {
            [ContainerStateSave.SectionName] = System.Text.Encoding.UTF8.GetBytes("{\"version\":1,\"player\":[],\"containers\":[]}"),
        };
        var loaded = new ContainerLedger();
        loaded.MarkCaught("stale");
        ContainerStateSave.Apply(older, loaded, new PlayerItems());
        Assert.Empty(loaded.Caught);
    }

    [Theory]
    [InlineData("\"caught\":[\"\"]")]
    [InlineData("\"caught\":\"npc1\"")]
    [InlineData("\"caught\":[7]")]
    public void A_malformed_caught_list_fails_the_load_and_leaves_the_ledger_alone(string caught)
    {
        var sections = new Dictionary<string, byte[]>
        {
            [ContainerStateSave.SectionName] = System.Text.Encoding.UTF8.GetBytes("{\"version\":1,\"player\":[],\"containers\":[]," + caught + "}"),
        };
        var ledger = new ContainerLedger();
        ledger.Freeze("kept", new[] { new InventoryStack(Gun, 1) });

        Assert.Throws<FormatException>(() => ContainerStateSave.Apply(sections, ledger, new PlayerItems()));

        Assert.Equal(new[] { "kept" }, ledger.References);
        Assert.Empty(ledger.Caught);
    }

    [Fact]
    public void Stolen_items_stay_gone_when_the_same_actor_is_looted_as_a_corpse_after_a_save()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        Open(Sleeper, () => 38, ledger: ledger, player: player).Transfer(ContainerSide.Container, Gun, 1);

        var sections = ContainerStateSave.Sections(ledger, player);
        var loadedLedger = new ContainerLedger();
        var loadedPlayer = new PlayerItems();
        ContainerStateSave.Apply(sections, loadedLedger, loadedPlayer);
        var corpse = ActorInventory.Open("npc1", "base", new Actors(), loadedLedger, loadedPlayer, Caps, _ => 1, out var refusal)!;

        Assert.Equal(string.Empty, refusal);
        Assert.Equal(new[] { new InventoryStack(Gun, 2), new InventoryStack(Caps, 5) }, corpse.ContainerStacks);
        Assert.Equal(1, loadedPlayer.CountOf(Gun));
        Assert.Equal(TransferStatus.Done, corpse.Transfer(ContainerSide.Container, Caps, 5).Status);
        Assert.Equal(5, loadedPlayer.CountOf(Caps));
    }
}
