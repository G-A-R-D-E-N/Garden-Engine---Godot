using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VendorRestockTests
{
    const string Caps = "00000f:fo4.esm";
    const string Gun = "000100:a.esm";
    const string Chest = "000900:a.esm";

    sealed class Actors : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => new ActorSetup("Vic", true, [new("ignored:a.esm", 9)]);
    }

    sealed class Chests : IContainerRecords
    {
        public ContainerSetup? Read(string refKey, string baseKey) => new ContainerSetup("Chest", false, false, [new(Gun, 2), new(Caps, 100)]);
    }

    static ActorInventory Open(ContainerLedger ledger, PlayerItems player, VendorRestock restock, double hours)
    {
        var inventory = ActorInventory.OpenVendor("npc1", "base", Chest, "chestbase", new Actors(), new Chests(), ledger, player, Caps, BarterPricing.Factor, out var refusal, restock, hours);
        Assert.Equal(string.Empty, refusal);
        return inventory!;
    }

    // Detects: vendor stock that never comes back; a visit 48 hours later must restore the chest record's stock and caps.
    [Fact]
    public void A_visit_after_the_interval_resets_the_stock()
    {
        var ledger = new ContainerLedger();
        var restock = new VendorRestock();
        var player = new PlayerItems();
        Open(ledger, player, restock, 10);
        ledger.Find(Chest)!.Remove(Gun, 2);
        ledger.Find(Chest)!.Remove(Caps, 100);
        Open(ledger, player, restock, 40);
        Assert.Equal(0, ledger.Find(Chest)!.CountOf(Gun));
        Open(ledger, player, restock, 58);
        Assert.Equal(2, ledger.Find(Chest)!.CountOf(Gun));
        Assert.Equal(100, ledger.Find(Chest)!.CountOf(Caps));
    }

    [Fact]
    public void Without_the_restock_arguments_the_chest_is_never_refreshed()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        ActorInventory.OpenVendor("npc1", "base", Chest, "chestbase", new Actors(), new Chests(), ledger, player, Caps, BarterPricing.Factor, out _);
        ledger.Find(Chest)!.Remove(Gun, 2);
        ActorInventory.OpenVendor("npc1", "base", Chest, "chestbase", new Actors(), new Chests(), ledger, player, Caps, BarterPricing.Factor, out _);
        Assert.Equal(0, ledger.Find(Chest)!.CountOf(Gun));
    }

    [Fact]
    public void A_clock_that_moved_back_counts_as_due()
    {
        var restock = new VendorRestock();
        restock.Mark(Chest, 100);
        Assert.True(restock.Due(Chest, 50));
        Assert.False(restock.Due(Chest, 120));
    }
}
