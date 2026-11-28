using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlacedContainerTests
{
    const string Ref = "000ABC:Fallout4.esm";
    const string Base = "000123:Fallout4.esm";

    sealed class FakeRecords : IContainerRecords
    {
        public ContainerSetup? Setup { get; set; } = new("Footlocker", false, false,
            new[] { new InventoryStack("A", 2), new InventoryStack("B", 12) });

        public int Reads { get; private set; }

        public ContainerSetup? Read(string refKey, string baseKey)
        {
            Reads++;
            return Setup;
        }
    }

    sealed class FakeItems : IItemRecords
    {
        public ItemRecord? Find(string id) => new(id, "MISC", "Item " + id, 1, 0.5);
    }

    static PlacedContainerInventory Open(FakeRecords records, ContainerLedger ledger, PlayerItems player) =>
        PlacedContainerInventory.Open(Ref, Base, records, ledger, player, out _)!;

    [Fact]
    public void Item_runs_come_from_the_typed_container_rows_in_stored_order_and_keep_the_stored_count()
    {
        var rows = new[]
        {
            new ItemAmount(new FormKey("Fallout4.esm", 0x800), 5),
            new ItemAmount(new FormKey("Mod.esp", 0x01000000), -1),
        };
        var runs = RadicleContainerRecords.Runs(rows);
        Assert.Equal(new[] { new InventoryStack("000800:Fallout4.esm", 5), new InventoryStack("1000000:Mod.esp", -1) }, runs);
        Assert.Empty(RadicleContainerRecords.Runs(Array.Empty<ItemAmount>()));
    }

    [Fact]
    public void Item_runs_stop_at_the_run_cap()
    {
        var rows = Enumerable.Range(1, RadicleContainerRecords.MaxItemRuns + 10).Select(i => new ItemAmount(new FormKey("Fallout4.esm", (uint)i), 1));
        Assert.Equal(RadicleContainerRecords.MaxItemRuns, RadicleContainerRecords.Runs(rows).Count);
    }

    [Fact]
    public void A_reference_without_an_owner_takes_the_cell_owner_and_rank_and_its_own_owner_wins()
    {
        var own = new FormKey("Fallout4.esm", 0x44);
        var cell = new CellOwner(new FormKey("Fallout4.esm", 0x55), 3);

        Assert.Equal((cell.Owner, cell.Rank), ContainerOwnership.Effective(null, 9, () => cell));
        Assert.Equal((own, 1), ContainerOwnership.Effective(own, 1, () => throw new InvalidOperationException("the cell must not be read")));
        Assert.Equal((null, 9), ContainerOwnership.Effective(null, 9, () => null));
    }

    [Fact]
    public void A_cell_owner_makes_an_unowned_container_theft_unless_the_player_passes_its_rank()
    {
        var cell = new CellOwner(new FormKey("Fallout4.esm", 0x55), 3);
        Func<FormKey, int, bool> allows = (_, rank) => rank <= 2;

        var (owner, rank) = ContainerOwnership.Effective(null, null, () => cell);
        Assert.True(ContainerOwnership.IsTheft(owner, null, rank, allows));
        var (none, noRank) = ContainerOwnership.Effective(null, null, () => null);
        Assert.False(ContainerOwnership.IsTheft(none, null, noRank, allows));
        var (low, lowRank) = ContainerOwnership.Effective(null, null, () => cell with { Rank = 1 });
        Assert.False(ContainerOwnership.IsTheft(low, null, lowRank, allows));
    }

    static Radicle.Interop.CellInfo CellWith(FormKey? owner, int? rank) =>
        new(null, null, null, null, null, null, null, null, null, null, Owner: owner, OwnerRank: rank);

    [Theory]
    [InlineData(3, 3)]
    [InlineData(null, null)]
    public void A_cell_owner_is_taken_from_the_cell_owner_and_rank(int? rank, int? expected)
    {
        var parsed = CellOwnership.From(CellWith(new FormKey("Fallout4.esm", 0x55), rank));

        Assert.Equal(new FormKey("Fallout4.esm", 0x55), parsed!.Value.Owner);
        Assert.Equal(expected, parsed.Value.Rank);
    }

    [Fact]
    public void A_missing_or_zero_local_cell_owner_is_no_owner()
    {
        Assert.Null(CellOwnership.From(CellWith(null, 3)));
        Assert.Null(CellOwnership.From(CellWith(new FormKey("Fallout4.esm", 0), 3)));
    }

    [Fact]
    public void An_owned_container_is_theft_unless_no_crime_or_the_player_passes_the_owner_rule()
    {
        var owner = new FormKey("Fallout4.esm", 0x44);
        var seen = new List<(FormKey, int)>();
        Func<FormKey, int, bool> allows = (who, rank) => { seen.Add((who, rank)); return rank <= 2; };

        Assert.False(ContainerOwnership.IsTheft(null, null, null, allows));
        Assert.True(ContainerOwnership.IsTheft(owner, null, null, null));
        Assert.True(ContainerOwnership.IsTheft(owner, false, 5, allows));
        Assert.False(ContainerOwnership.IsTheft(owner, false, 2, allows));
        Assert.False(ContainerOwnership.IsTheft(owner, false, null, allows));
        Assert.False(ContainerOwnership.IsTheft(owner, true, 5, allows));
        Assert.Equal(new[] { (owner, 5), (owner, 2), (owner, 0) }, seen);
    }

    [Fact]
    public void Expand_merges_stacks_drops_empty_ones_and_rolls_each_leveled_list_with_the_reference_seed()
    {
        var seeds = new List<ulong>();
        var items = new[] { new InventoryStack("A", 1), new InventoryStack("L", 2), new InventoryStack("A", 3), new InventoryStack("Z", 0), new InventoryStack("N", -4) };

        var stacks = ContainerContents.Expand(items, Ref, id => id == "L", (list, count, seed) =>
        {
            seeds.Add(seed);
            return new[] { new InventoryStack("C", count), new InventoryStack("A", 1) };
        });

        Assert.Equal(new[] { new InventoryStack("A", 5), new InventoryStack("C", 2) }, stacks);
        Assert.Equal(new[] { ContainerContents.SeedOf(Ref) }, seeds);
        Assert.Equal(ContainerContents.SeedOf(Ref), ContainerContents.SeedOf(Ref.ToLowerInvariant()));
        Assert.NotEqual(ContainerContents.SeedOf(Ref), ContainerContents.SeedOf("000ABD:Fallout4.esm"));
    }

    [Fact]
    public void Stacks_multiply_each_row_count_by_the_pick_multiplier_clamp_and_drop_empty_rows()
    {
        var entries = new[] { ("A", 20), ("B", 0), ("C", int.MaxValue) };

        Assert.Equal(new[] { new InventoryStack("A", 20), new InventoryStack("C", int.MaxValue) }, ContainerContents.Stacks(entries, 1));
        Assert.Equal(new[] { new InventoryStack("A", 60), new InventoryStack("C", int.MaxValue) }, ContainerContents.Stacks(entries, 3));
        Assert.Empty(ContainerContents.Stacks(Array.Empty<(string, int)>(), 5));
    }

    [Fact]
    public void Taking_moves_stock_to_the_player_and_bumps_the_revision()
    {
        var player = new PlayerItems();
        var inventory = Open(new FakeRecords(), new ContainerLedger(), player);

        var result = inventory.Transfer(ContainerSide.Container, "B", 5);

        Assert.Equal(TransferStatus.Done, result.Status);
        Assert.Equal(7, inventory.ContainerStacks.Single(stack => stack.Id == "B").Count);
        Assert.Equal(5, player.CountOf("B"));
        Assert.Equal(1, inventory.Revision);
        Assert.NotEqual(string.Empty, inventory.Refusal(ContainerSide.Container, "B", 8));
        Assert.Equal(TransferStatus.Refused, inventory.Transfer(ContainerSide.Container, "B", 8).Status);
        Assert.Equal(1, inventory.Revision);
    }

    [Fact]
    public void Storing_moves_player_stock_into_the_container_and_a_stack_that_empties_disappears()
    {
        var player = new PlayerItems();
        player.Add("P", 3);
        var inventory = Open(new FakeRecords(), new ContainerLedger(), player);

        Assert.Equal(TransferStatus.Done, inventory.Transfer(ContainerSide.Player, "P", 3).Status);
        Assert.Empty(inventory.PlayerStacks);
        Assert.Equal(3, inventory.ContainerStacks.Single(stack => stack.Id == "P").Count);
        Assert.Equal(TransferStatus.Done, inventory.Transfer(ContainerSide.Container, "A", 2).Status);
        Assert.DoesNotContain(inventory.ContainerStacks, stack => stack.Id == "A");
    }

    [Fact]
    public void A_reopened_container_keeps_what_the_player_changed_and_does_not_reroll_its_contents()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        var records = new FakeRecords();
        Open(records, ledger, player).Transfer(ContainerSide.Container, "A", 2);
        records.Setup = records.Setup! with { Stacks = new[] { new InventoryStack("REROLLED", 9) } };

        var again = Open(records, ledger, player);

        Assert.DoesNotContain(again.ContainerStacks, stack => stack.Id is "A" or "REROLLED");
        Assert.Equal(12, again.ContainerStacks.Single(stack => stack.Id == "B").Count);
    }

    [Fact]
    public void An_owned_container_opens_in_steal_mode_and_a_locked_or_unreadable_one_does_not_open()
    {
        var records = new FakeRecords { Setup = new ContainerSetup("Safe", true, false, new[] { new InventoryStack("A", 1) }) };
        Assert.Equal(ContainerMode.Steal, Open(records, new ContainerLedger(), new PlayerItems()).Mode);
        Assert.Equal(ContainerMode.Loot, Open(new FakeRecords(), new ContainerLedger(), new PlayerItems()).Mode);

        records.Setup = records.Setup with { Locked = true };
        Assert.Null(PlacedContainerInventory.Open(Ref, Base, records, new ContainerLedger(), new PlayerItems(), out var locked));
        Assert.Equal(PlacedContainerInventory.LockedText, locked);

        records.Setup = null;
        Assert.Null(PlacedContainerInventory.Open(Ref, Base, records, new ContainerLedger(), new PlayerItems(), out var missing));
        Assert.Equal(PlacedContainerInventory.MissingText, missing);
    }

    [Fact]
    public void Container_and_player_state_round_trips_through_the_save_section()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        Open(new FakeRecords(), ledger, player).Transfer(ContainerSide.Container, "B", 4);

        var sections = ContainerStateSave.Sections(ledger, player);
        var loadedLedger = new ContainerLedger();
        var loadedPlayer = new PlayerItems();
        loadedPlayer.Add("STALE", 1);
        ContainerStateSave.Apply(sections, loadedLedger, loadedPlayer);

        Assert.Equal(new[] { new InventoryStack("B", 4) }, loadedPlayer.Stacks);
        Assert.Equal(new[] { new InventoryStack("A", 2), new InventoryStack("B", 8) }, loadedLedger.Find(Ref)!.Stacks);
        var reopened = Open(new FakeRecords { Setup = new ContainerSetup("Footlocker", false, false, Array.Empty<InventoryStack>()) }, loadedLedger, loadedPlayer);
        Assert.Equal(8, reopened.ContainerStacks.Single(stack => stack.Id == "B").Count);
    }

    [Fact]
    public void A_save_without_the_section_empties_the_state_and_a_malformed_one_changes_nothing()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        player.Add("P", 2);
        ledger.Freeze(Ref, new[] { new InventoryStack("A", 1) });

        Assert.Empty(ContainerStateSave.Sections(new ContainerLedger(), new PlayerItems()));
        var bad = new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = System.Text.Encoding.UTF8.GetBytes("{\"version\":1,\"player\":[{\"id\":\"X\",\"count\":0}],\"containers\":[]}") };
        Assert.Throws<FormatException>(() => ContainerStateSave.Apply(bad, ledger, player));
        Assert.Equal(2, player.CountOf("P"));
        Assert.NotNull(ledger.Find(Ref));
        var wrongVersion = new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = System.Text.Encoding.UTF8.GetBytes("{\"version\":9,\"player\":[],\"containers\":[]}") };
        Assert.Throws<FormatException>(() => ContainerStateSave.Apply(wrongVersion, ledger, player));
        Assert.Throws<FormatException>(() => ContainerStateSave.Apply(new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = new byte[] { 1, 2 } }, ledger, player));

        ContainerStateSave.Apply(new Dictionary<string, byte[]>(), ledger, player);
        Assert.Empty(player.Stacks);
        Assert.Null(ledger.Find(Ref));
    }

    [Fact]
    public void The_container_controller_takes_through_the_record_rules_and_the_ledger_remembers()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        var inventory = Open(new FakeRecords(), ledger, player);
        var controller = new ContainerController(new RecordContainerRules(inventory, new FakeItems()));

        Assert.Equal("Item A x 2", WorkshopTestHelpers.RowTexts(controller.List)[0]);
        controller.Handle(MenuInputAction.Accept);

        Assert.Equal(1, player.CountOf("A"));
        Assert.Equal(1, ledger.Find(Ref)!.CountOf("A"));
        controller.Handle(MenuInputAction.Back);
        Assert.Equal(ContainerOutcome.Closed, controller.Outcome);
    }
}

public class PlayerInventoryItemsTests
{
    sealed class Items : OpenCommonwealth.Services.Ui.Records.IItemRecords
    {
        public OpenCommonwealth.Services.Ui.Records.ItemRecord? Find(string id) =>
            id == "gone" ? null : new(id, id == "w" ? "WEAP" : "MISC", "Name " + id, 10, 2.5);
    }

    [Fact]
    public void Taken_stacks_become_pipboy_items_with_record_name_category_weight_and_value()
    {
        var items = OpenCommonwealth.Services.Ui.PipBoy.PlayerInventoryItems.Build(
            new[] { new InventoryStack("w", 2), new InventoryStack("m", 1), new InventoryStack("gone", 3), new InventoryStack("z", 0) }, new Items());

        Assert.Equal(new[] { "w", "m", "gone" }, items.Select(item => item.Id));
        Assert.Equal(("Name w", "Weapons", 2.5, 10.0, 2), (items[0].Name, items[0].Category, items[0].Weight, items[0].Value, items[0].Count));
        Assert.Equal("Misc", items[1].Category);
        Assert.Equal(("gone", 0.0, 0.0), (items[2].Name, items[2].Weight, items[2].Value));
    }
}

public class SharedInventoryRevisionTests
{
    [Fact]
    public void OwnTransferAfterExternalChangeDoesNotReuseAnEarlierRevision()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        var inventory = PlacedContainerInventory.Open(Crate, "000123:Fallout4.esm", new Crates(), ledger, player, out _)!;
        player.Add(Tea, 1);
        var afterExternalChange = inventory.Revision;

        Assert.Equal(TransferStatus.Done, inventory.Transfer(ContainerSide.Container, "A", 1).Status);
        Assert.NotEqual(afterExternalChange, inventory.Revision);
    }

    const string Caps = "00000f:fo4.esm";
    const string Gun = "000100:a.esm";
    const string Tea = "000101:a.esm";
    const string Crate = "000ABC:Fallout4.esm";

    sealed class Items : IItemRecords
    {
        public ItemRecord? Find(string id) => new(id, "MISC", "Item " + id, 10, 0.5);
    }

    sealed class Crates : IContainerRecords
    {
        public ContainerSetup? Read(string refKey, string baseKey) => new("Footlocker", false, false, [new("A", 9), new("B", 3)]);
    }

    sealed class Vendor : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => new("Vic", true, [new(Gun, 1), new(Caps, 50)]);
    }

    static ActorInventory OpenVendor(ContainerLedger ledger, PlayerItems player) =>
        ActorInventory.Open("npc1", "base", new Vendor(), ledger, player, Caps, side => side == BarterSide.Player ? 0.5 : 1.5, out _)!;

    [Fact]
    public void Every_mutation_of_a_player_items_list_advances_its_revision_and_a_refused_one_does_not()
    {
        var items = new PlayerItems();
        Assert.Equal(0, items.Revision);

        items.Add(Tea, 2);
        var added = items.Revision;
        Assert.True(added > 0);

        Assert.False(items.Remove(Tea, 5));
        Assert.Equal(added, items.Revision);

        Assert.True(items.Remove(Tea, 1));
        var removed = items.Revision;
        Assert.True(removed > added);

        var other = new PlayerItems();
        Assert.True(items.MoveTo(other, Tea, 1));
        Assert.True(items.Revision > removed);
        Assert.True(other.Revision > 0);

        var before = items.Revision;
        items.Replace([new(Gun, 1)]);
        Assert.True(items.Revision > before);
    }

    [Fact]
    public void A_vendor_inventory_revision_moves_when_the_player_or_the_vendor_row_changes_through_another_holder()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        var inventory = OpenVendor(ledger, player);
        var open = inventory.Revision;

        player.Add(Tea, 1);
        var afterPlayer = inventory.Revision;
        Assert.NotEqual(open, afterPlayer);

        ledger.Find("npc1")!.Add(Tea, 3);
        Assert.NotEqual(afterPlayer, inventory.Revision);
    }

    [Fact]
    public void A_container_inventory_revision_moves_when_the_player_or_the_container_row_changes_through_another_holder()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        var inventory = PlacedContainerInventory.Open(Crate, "000123:Fallout4.esm", new Crates(), ledger, player, out _)!;
        var open = inventory.Revision;

        player.Add(Tea, 1);
        var afterPlayer = inventory.Revision;
        Assert.NotEqual(open, afterPlayer);

        ledger.Find(Crate)!.Add("C", 1);
        Assert.NotEqual(afterPlayer, inventory.Revision);
    }

    [Fact]
    public void An_own_transfer_costs_exactly_one_revision_and_a_refused_one_costs_none()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        var inventory = PlacedContainerInventory.Open(Crate, "000123:Fallout4.esm", new Crates(), ledger, player, out _)!;
        var open = inventory.Revision;

        Assert.Equal(TransferStatus.Done, inventory.Transfer(ContainerSide.Container, "A", 2).Status);
        Assert.Equal(open + 1, inventory.Revision);
        Assert.Equal(TransferStatus.Refused, inventory.Transfer(ContainerSide.Container, "A", 99).Status);
        Assert.Equal(open + 1, inventory.Revision);
    }

    [Fact]
    public void An_open_loot_menu_refreshes_when_a_script_side_holder_empties_the_staged_container_stack()
    {
        var ledger = new ContainerLedger();
        var inventory = PlacedContainerInventory.Open(Crate, "000123:Fallout4.esm", new Crates(), ledger, new PlayerItems(), out _)!;
        var controller = new ContainerController(new RecordContainerRules(inventory, new Items()));
        controller.Handle(MenuInputAction.Accept);

        Assert.True(ledger.Find(Crate)!.Remove("A", 9));
        controller.Tick(0.1);

        Assert.Equal(ContainerController.ChangedText, controller.Message);
    }

    [Fact]
    public void An_open_barter_menu_prunes_a_staged_line_when_a_script_side_holder_removes_the_stock_behind_it()
    {
        var player = new PlayerItems();
        player.Add(Tea, 2);
        var inventory = OpenVendor(new ContainerLedger(), player);
        var controller = new BarterController(new RecordBarterRules(inventory, new Items()));
        controller.Handle(MenuInputAction.Accept);
        controller.Handle(MenuInputAction.Accept);
        Assert.Equal(2, controller.Basket.Single().Count);

        Assert.True(player.Remove(Tea, 1));
        controller.Tick(0.1);

        Assert.Equal(BarterController.ChangedText, controller.Message);
        Assert.Equal(1, controller.Basket.Single().Count);
    }
}
