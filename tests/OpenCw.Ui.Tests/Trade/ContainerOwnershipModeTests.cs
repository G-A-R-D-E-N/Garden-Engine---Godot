using System;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ContainerOwnershipModeTests
{
    const string Ref = "000ABC:Fallout4.esm";
    const string Base = "000123:Fallout4.esm";

    sealed class OwnedRecords(FormKey? owner, bool? noCrime, int? rank, Func<FormKey, int, bool>? allows, bool locked = false) : IContainerRecords
    {
        public ContainerSetup? Read(string refKey, string baseKey) =>
            new("Footlocker", ContainerOwnership.IsTheft(owner, noCrime, rank, allows), locked, new[] { new InventoryStack("A", 2) });
    }

    static readonly FormKey Faction = new("Fallout4.esm", 0x44);

    static ContainerMode ModeOf(OwnedRecords records) =>
        PlacedContainerInventory.Open(Ref, Base, records, new ContainerLedger(), new PlayerItems(), out _)!.Mode;

    [Fact]
    public void A_faction_container_opens_as_loot_when_the_player_rank_passes_and_as_steal_when_it_does_not()
    {
        Func<FormKey, int, bool> allowsUpToRankTwo = (_, rank) => rank <= 2;

        Assert.Equal(ContainerMode.Loot, ModeOf(new OwnedRecords(Faction, false, 2, allowsUpToRankTwo)));
        Assert.Equal(ContainerMode.Steal, ModeOf(new OwnedRecords(Faction, false, 3, allowsUpToRankTwo)));
    }

    [Fact]
    public void An_owned_container_is_steal_without_a_rule_loot_when_unowned_or_no_crime()
    {
        Assert.Equal(ContainerMode.Steal, ModeOf(new OwnedRecords(Faction, false, null, null)));
        Assert.Equal(ContainerMode.Loot, ModeOf(new OwnedRecords(null, null, null, null)));
        Assert.Equal(ContainerMode.Loot, ModeOf(new OwnedRecords(Faction, true, 9, (_, _) => false)));
    }

    [Fact]
    public void A_locked_faction_container_still_refuses_until_the_ledger_marks_it_unlocked_whoever_owns_it()
    {
        var records = new OwnedRecords(Faction, false, 0, (_, _) => true, locked: true);
        var ledger = new ContainerLedger();

        Assert.Null(PlacedContainerInventory.Open(Ref, Base, records, ledger, new PlayerItems(), out var refusal));
        Assert.Equal(PlacedContainerInventory.LockedText, refusal);

        ledger.MarkUnlocked(Ref);

        Assert.Equal(ContainerMode.Loot, PlacedContainerInventory.Open(Ref, Base, records, ledger, new PlayerItems(), out _)!.Mode);
    }
}

public class ActorLootModelTests
{
    const string Gun = "000100:a.esm";
    const string Caps = "00000f:fo4.esm";

    sealed class Actors(bool alive) : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => new("Vic", alive, [new(Gun, 2), new(Caps, 7)]);
    }

    sealed class Items : IItemRecords
    {
        public ItemRecord? Find(string id) => new(id, "WEAP", "Item " + id, 30, 1.5);
    }

    [Fact]
    public void A_corpse_opens_in_the_container_model_with_its_name_loot_mode_and_named_stacks_and_a_take_moves_to_the_player()
    {
        var player = new PlayerItems();
        var inventory = ActorInventory.Open("npc1", "base", new Actors(false), new ContainerLedger(), player, Caps, BarterPricing.Factor, out var refusal)!;
        var rules = new RecordContainerRules(inventory, new Items());

        Assert.Equal(string.Empty, refusal);
        Assert.Equal("Vic", rules.Title);
        Assert.Equal(ContainerMode.Loot, rules.Mode);
        Assert.Equal("Item " + Gun, rules.ContainerStacks[0].Name);
        Assert.Equal(string.Empty, rules.Refusal(ContainerSide.Container, Gun, 2));

        Assert.Equal(TransferStatus.Done, rules.Transfer(ContainerSide.Container, Gun, 2).Status);
        Assert.Equal(2, player.CountOf(Gun));
        Assert.Single(rules.ContainerStacks);
        Assert.Equal(ActorInventory.NotEnoughText, rules.Refusal(ContainerSide.Container, Gun, 1));
    }

    [Fact]
    public void A_living_actor_is_refused_in_loot_mode_with_the_alive_text()
    {
        var living = ActorInventory.Open("npc1", "base", new Actors(true), new ContainerLedger(), new PlayerItems(), Caps, BarterPricing.Factor, out _)!;

        Assert.Equal(ActorInventory.AliveText, new RecordContainerRules(living, new Items()).Refusal(ContainerSide.Container, Gun, 1));
    }
}
