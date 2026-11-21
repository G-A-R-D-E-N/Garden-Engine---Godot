using System.Text;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class StolenStateTests
{
    const string Settlers = "000044:Fallout4.esm";
    const string Gun = "000100:a.esm";
    const string Ref = "000ABC:Fallout4.esm";
    const string Chest = "000ABD:Fallout4.esm";

    sealed class Facts : ICrimeFacts
    {
        public string? FactionOf(string owner) => owner == Settlers ? owner : null;

        public int ValueOf(string item) => 10;

        public double? StealMultiplier => 1;
    }

    static CrimeLedger Ledger() => new() { Facts = new Facts(), Formula = CrimeLedger.BountyFor };

    static ActivationTarget Loose(string? owner = Settlers, int count = 2) =>
        new(Ref, Gun, "Weapon", new HashSet<string>(), count, owner, false, 0);

    [Fact]
    public void AnUnknownOwnershipAnswerIsNeitherTheftNorAStolenCopy()
    {
        var items = new PlayerItems();
        var ledger = Ledger();
        var pickup = new ItemPickup(items, _ => false, _ => { }, crimes: ledger, decide: _ => OwnershipDecision.Unknown);

        var result = pickup.Activate(Loose());

        Assert.Equal(ActivationStatus.Handled, result.Status);
        Assert.Equal("took 2", result.Detail);
        Assert.Empty(ledger.Events);
        Assert.Equal(2, items.CountOf(Gun, ItemState.Plain));
    }

    [Fact]
    public void ADeniedOwnershipAnswerReportsTheftAndKeepsTheCopyStolen()
    {
        var items = new PlayerItems();
        items.Add(Gun, ItemState.Plain, 1);
        var ledger = Ledger();
        var pickup = new ItemPickup(items, _ => true, _ => { }, crimes: ledger, decide: _ => OwnershipDecision.Denied);

        pickup.Activate(Loose());

        Assert.Single(ledger.Events);
        Assert.Equal(1, items.CountOf(Gun, ItemState.Plain));
        Assert.Equal(2, items.CountOf(Gun, ItemState.Plain.WithStolenFrom(Settlers)));
        Assert.Equal(3, items.CountOf(Gun));
        Assert.Equal(2, items.Entries.Count);
    }

    [Fact]
    public void ThePlainCopyLeavesFirstSoTheStolenOnesStayWithThePlayer()
    {
        var items = new PlayerItems();
        items.Add(Gun, ItemState.Plain.WithStolenFrom(Settlers), 2);
        items.Add(Gun, ItemState.Plain, 1);

        Assert.True(items.Remove(Gun, 1));

        Assert.Equal(0, items.CountOf(Gun, ItemState.Plain));
        Assert.Equal(2, items.CountOf(Gun, ItemState.Plain.WithStolenFrom(Settlers)));
    }

    [Fact]
    public void AStolenCopySurvivesTheContainerStateSaveAndAnOldRowStillLoads()
    {
        var player = new PlayerItems();
        player.Add(Gun, ItemState.Plain.WithStolenFrom(Settlers), 2);
        player.Add(Gun, ItemState.Plain, 1);

        var saved = ContainerStateSave.Sections(new ContainerLedger(), player);
        var loaded = new PlayerItems();
        ContainerStateSave.Apply(saved, new ContainerLedger(), loaded);

        Assert.Equal(2, loaded.CountOf(Gun, ItemState.Plain.WithStolenFrom(Settlers)));
        Assert.Equal(1, loaded.CountOf(Gun, ItemState.Plain));

        const string old = "{\"version\":1,\"player\":[{\"id\":\"000100:a.esm\",\"count\":2}],\"containers\":[]}";
        var legacy = new PlayerItems();
        ContainerStateSave.Apply(new Dictionary<string, byte[]> { [ContainerStateSave.SectionName] = Encoding.UTF8.GetBytes(old) }, new ContainerLedger(), legacy);
        Assert.Equal(2, legacy.CountOf(Gun, ItemState.Plain));
    }

    sealed class ChestRecords(bool owned) : IContainerRecords
    {
        public ContainerSetup? Read(string refKey, string baseKey) =>
            new("Footlocker", owned, false, new[] { new InventoryStack(Gun, 4) }, Settlers);
    }

    static PlacedContainerInventory Open(PlayerItems player, CrimeLedger ledger, bool owned = true) =>
        PlacedContainerInventory.Open(Chest, "000123:Fallout4.esm", new ChestRecords(owned), new ContainerLedger(), player, out _, crimes: ledger)!;

    [Fact]
    public void TakingBackWhatThePlayerDepositedIsNotATheft()
    {
        var player = new PlayerItems();
        player.Add(Gun, ItemState.Plain, 2);
        var ledger = Ledger();
        var container = Open(player, ledger);

        Assert.Equal(TransferStatus.Done, container.Transfer(ContainerSide.Player, Gun, 2).Status);
        Assert.Equal(TransferStatus.Done, container.Transfer(ContainerSide.Container, Gun, 2).Status);

        Assert.Empty(ledger.Events);
        Assert.Equal(2, player.CountOf(Gun, ItemState.Plain));
    }

    [Fact]
    public void ADepositIsTakenBackBeforeTheContainersOwnStockAndOnlyTheRestIsTheft()
    {
        var player = new PlayerItems();
        player.Add(Gun, ItemState.Plain, 1);
        var ledger = Ledger();
        var container = Open(player, ledger);
        container.Transfer(ContainerSide.Player, Gun, 1);

        container.Transfer(ContainerSide.Container, Gun, 1);
        Assert.Empty(ledger.Events);

        container.Transfer(ContainerSide.Player, Gun, 1);
        container.Transfer(ContainerSide.Container, Gun, 5);

        var crime = Assert.Single(ledger.Events);
        Assert.Equal(4, crime.Count);
        Assert.Equal(1, player.CountOf(Gun, ItemState.Plain));
        Assert.Equal(4, player.CountOf(Gun, ItemState.Plain.WithStolenFrom(Settlers)));
    }

    [Fact]
    public void TakingFromAnOwnedContainerMarksTheCopiesStolenFromItsOwner()
    {
        var player = new PlayerItems();
        var container = Open(player, Ledger());

        container.Transfer(ContainerSide.Container, Gun, 3);

        Assert.Equal(3, player.CountOf(Gun, ItemState.Plain.WithStolenFrom(Settlers)));
        Assert.Equal(0, player.CountOf(Gun, ItemState.Plain));
    }

    [Fact]
    public void ADepositIntoAnUnownedContainerIsNotMarkedAndTakingFromItReportsNothing()
    {
        var player = new PlayerItems();
        player.Add(Gun, ItemState.Plain, 1);
        var ledger = Ledger();
        var container = Open(player, ledger, owned: false);

        container.Transfer(ContainerSide.Player, Gun, 1);
        container.Transfer(ContainerSide.Container, Gun, 5);

        Assert.Empty(ledger.Events);
        Assert.Equal(5, player.CountOf(Gun, ItemState.Plain));
    }

    const string Caps = "00000f:fo4.esm";
    const string Vendor = "000555:a.esm";
    const string VendorBase = "000556:a.esm";
    const string Stock = "000900:a.esm";

    sealed class Vendors : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => new("Vic", true, [new("ignored:a.esm", 9)]);
    }

    sealed class Stocks : IContainerRecords
    {
        public ContainerSetup? Read(string refKey, string baseKey) => new("Chest", false, false, [new(Caps, 1000)]);
    }

    static ActorInventory Vend(PlayerItems player, ContainerLedger ledger) =>
        ActorInventory.OpenVendor(Vendor, VendorBase, Stock, "chestbase", new Vendors(), new Stocks(), ledger, player, Caps, BarterPricing.Factor, out _)!;

    [Fact]
    public void AVendorNeitherListsNorBuysBackTheCopiesStolenFromIt()
    {
        var player = new PlayerItems();
        player.Add(Gun, ItemState.Plain.WithStolenFrom(Vendor), 1);
        player.Add(Gun, ItemState.Plain.WithStolenFrom(VendorBase), 1);
        player.Add(Gun, ItemState.Plain, 1);
        var vendor = Vend(player, new ContainerLedger());
        var ledger = (IBarterLedger)vendor;

        Assert.Equal(new[] { new InventoryStack(Gun, 1) }, ledger.PlayerStacks);
        Assert.Equal(ActorInventory.RecognisedText, ledger.Commit([new BarterLine(BarterSide.Player, Gun, 2)], 0).Reason);
        Assert.True(ledger.Commit([new BarterLine(BarterSide.Player, Gun, 1)], 0).Ok);
        Assert.Equal(1, player.CountOf(Gun, ItemState.Plain.WithStolenFrom(Vendor)));
        Assert.Equal(1, player.CountOf(Gun, ItemState.Plain.WithStolenFrom(VendorBase)));
        Assert.Equal(ActorInventory.RecognisedText, ledger.Refusal(BarterSide.Player, Gun));
    }

    [Fact]
    public void ACopyStolenFromSomeoneElseSellsAndLosesItsStolenMark()
    {
        var player = new PlayerItems();
        player.Add(Gun, ItemState.Plain.WithStolenFrom(Settlers), 2);
        var held = new ContainerLedger();
        var vendor = Vend(player, held);

        Assert.True(((IBarterLedger)vendor).Commit([new BarterLine(BarterSide.Player, Gun, 2)], 0).Ok);

        Assert.Equal(0, player.CountOf(Gun));
        Assert.Equal(2, held.Find(Stock)!.CountOf(Gun, ItemState.Plain));
    }

    [Fact]
    public void TheNotStolenCopiesAreSoldFirstWhenTheVendorRecognisesSome()
    {
        var player = new PlayerItems();
        player.Add(Gun, ItemState.Plain.WithStolenFrom(Vendor), 2);
        player.Add(Gun, ItemState.Plain, 3);
        var vendor = Vend(player, new ContainerLedger());

        Assert.True(((IBarterLedger)vendor).Commit([new BarterLine(BarterSide.Player, Gun, 3)], 0).Ok);

        Assert.Equal(2, player.CountOf(Gun, ItemState.Plain.WithStolenFrom(Vendor)));
        Assert.Equal(0, player.CountOf(Gun, ItemState.Plain));
    }
}
