using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ActorDeathLootTests
{
    const string Caps = "00000f:Fallout4.esm";
    const string Gun = "000040:Fallout4.esm";
    const string Armor = "000021:Fallout4.esm";
    const string Chem = "000031:Fallout4.esm";

    sealed class Actors(ActorSetup? setup) : IActorRecords
    {
        public int Reads { get; private set; }

        public ActorSetup? Read(string refKey, string baseKey)
        {
            Reads++;
            return setup;
        }
    }

    static InventoryStack Stack(string id, int count) => new(id, count);

    static ActorSetup Corpse(bool complete = true) => new("Vic", false, [Stack(Gun, 1)], complete, [Stack(Armor, 1), Stack(Chem, 2)]);

    static ActorInventory? Open(ActorSetup? setup, ContainerLedger ledger) =>
        ActorInventory.Open("npc1", "base", new Actors(setup), ledger, new PlayerItems(), Caps, _ => 1, out _);

    static IEnumerable<InventoryStack> Row(ContainerLedger ledger) => ledger.Find("npc1")!.Stacks.OrderBy(stack => stack.Id, System.StringComparer.Ordinal);

    [Fact]
    public void A_first_corpse_open_holds_the_carried_stacks_with_the_worn_and_death_stacks_and_marks_it()
    {
        var ledger = new ContainerLedger();

        Assert.NotNull(Open(Corpse(), ledger));

        Assert.Equal(new[] { Stack(Gun, 1), Stack(Armor, 1), Stack(Chem, 2) }.OrderBy(stack => stack.Id, System.StringComparer.Ordinal), Row(ledger));
        Assert.True(ledger.HasDeathLoot("npc1"));
    }

    [Fact]
    public void A_reopen_does_not_add_the_death_stacks_again_after_the_player_took_some()
    {
        var ledger = new ContainerLedger();
        Open(Corpse(), ledger)!.Transfer(ContainerSide.Container, Armor, 1);

        Open(Corpse(), ledger);

        Assert.Equal(0, ledger.Find("npc1")!.CountOf(Armor));
        Assert.Equal(2, ledger.Find("npc1")!.CountOf(Chem));
    }

    [Fact]
    public void A_living_read_freezes_only_the_carried_stacks_and_the_later_corpse_open_adds_the_rest_once()
    {
        var ledger = new ContainerLedger();
        ledger.Freeze("npc1", new[] { Stack(Gun, 1) });
        ledger.Find("npc1")!.Remove(Gun, 1);

        Open(new ActorSetup("Vic", false, [Stack(Gun, 1)]), ledger);
        Assert.False(ledger.HasDeathLoot("npc1"));
        Assert.Equal(0, ledger.Find("npc1")!.CountOf(Armor));

        Open(Corpse(), ledger);
        Open(Corpse(), ledger);

        Assert.Equal(new[] { Stack(Armor, 1), Stack(Chem, 2) }.OrderBy(stack => stack.Id, System.StringComparer.Ordinal), Row(ledger));
        Assert.True(ledger.HasDeathLoot("npc1"));
    }

    [Fact]
    public void An_incomplete_corpse_read_adds_and_marks_nothing()
    {
        var ledger = new ContainerLedger();
        ledger.Freeze("npc1", new[] { Stack(Gun, 1) });

        Open(Corpse(complete: false), ledger);

        Assert.False(ledger.HasDeathLoot("npc1"));
        Assert.Equal(new[] { Stack(Gun, 1) }, ledger.Find("npc1")!.Stacks);
    }

    [Fact]
    public void The_prompt_counts_death_stacks_not_yet_added_and_stops_once_the_row_is_taken_clean()
    {
        var ledger = new ContainerLedger();
        var empty = new ActorSetup("Vic", false, [], true, [Stack(Armor, 1)]);

        Assert.True(ActorLoot.HasItems(new Actors(empty), ledger, "npc1", "base"));

        ledger.Freeze("npc1", new List<InventoryStack>());
        Assert.True(ActorLoot.HasItems(new Actors(empty), ledger, "npc1", "base"));

        ledger.MarkDeathLoot("npc1");
        var reads = new Actors(empty);
        Assert.False(ActorLoot.HasItems(reads, ledger, "npc1", "base"));
        Assert.Equal(0, reads.Reads);
        Assert.False(ActorLoot.HasItems(new Actors(new ActorSetup("Vic", false, [], true, [])), new ContainerLedger(), "npc1", "base"));
    }

    [Fact]
    public void The_death_loot_mark_survives_save_and_load_and_a_save_without_it_clears_it()
    {
        var ledger = new ContainerLedger();
        Open(Corpse(), ledger);

        var loaded = new ContainerLedger();
        ContainerStateSave.Apply(ContainerStateSave.Sections(ledger, new PlayerItems()), loaded, new PlayerItems());

        Assert.True(loaded.HasDeathLoot("NPC1"));
        Open(Corpse(), loaded);
        Assert.Equal(1, loaded.Find("npc1")!.CountOf(Armor));

        ContainerStateSave.Apply(new Dictionary<string, byte[]>(), loaded, new PlayerItems());
        Assert.False(loaded.HasDeathLoot("npc1"));
    }

    [Fact]
    public void A_death_loot_mark_alone_makes_the_ledger_non_empty()
    {
        var ledger = new ContainerLedger();
        ledger.MarkDeathLoot("npc1");

        Assert.False(ledger.IsEmpty);
        Assert.Contains("deathLoot", System.Text.Encoding.UTF8.GetString(ContainerStateSave.Sections(ledger, new PlayerItems())[ContainerStateSave.SectionName]));
    }
}
