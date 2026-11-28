using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Dialogs;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ActorInventoryTests
{
    const string Caps = "00000f:fo4.esm";
    const string Gun = "000100:a.esm";
    const string Tea = "000101:a.esm";

    sealed class Records : IActorRecords
    {
        public int Reads { get; private set; }

        public bool Alive { get; set; }

        public HashSet<string> Partial { get; } = [];

        public ActorSetup? Read(string refKey, string baseKey)
        {
            Reads++;
            if (Partial.Contains(refKey)) return new ActorSetup("Vic", Alive, [new(Caps, 50)], false);
            return refKey == "bad" ? null : new ActorSetup("Vic", Alive, [new(Gun, 1), new(Caps, 50)]);
        }
    }

    sealed class Chests(string? key) : IVendorChests
    {
        public string? ChestKey(string npcRefKey, string npcBaseKey) => key;
    }

    static ActorInventory Open(Records records, ContainerLedger ledger, PlayerItems player)
    {
        var inventory = ActorInventory.Open("npc1", "base", records, ledger, player, Caps, side => side == BarterSide.Player ? 0.5 : 1.5, out var refusal);
        Assert.Equal(string.Empty, refusal);
        return inventory!;
    }

    static PlayerItems Player(params InventoryStack[] stacks)
    {
        var player = new PlayerItems();
        player.Replace(stacks);
        return player;
    }

    [Fact]
    public void OpenFreezesBaseStacksOnceAndHidesCaps()
    {
        var records = new Records();
        var ledger = new ContainerLedger();
        var inventory = Open(records, ledger, Player());

        Assert.Equal(new[] { new InventoryStack(Gun, 1) }, ((IBarterLedger)inventory).VendorStacks);
        Assert.Equal(50, inventory.VendorCaps);
        Assert.Equal(0.5, inventory.PriceFactor(BarterSide.Player));
        Assert.Equal(1.5, inventory.PriceFactor(BarterSide.Vendor));
        Assert.Equal(new[] { "npc1" }, ledger.References);
    }

    [Fact]
    public void ReopeningKeepsRuntimeChangesInsteadOfRereadingBaseStacks()
    {
        var ledger = new ContainerLedger();
        var first = Open(new Records(), ledger, Player());
        first.Add(Tea, 3);
        Assert.True(first.Remove(Gun, 1));

        var second = Open(new Records(), ledger, Player());

        Assert.Equal(new[] { new InventoryStack(Caps, 50), new InventoryStack(Tea, 3) }, second.ContainerStacks);
        Assert.Equal(2, first.Revision);
        Assert.False(first.Remove(Gun, 1));
    }

    [Fact]
    public void UnreadableActorRefusesToOpen()
    {
        var inventory = ActorInventory.Open("bad", "base", new Records(), new ContainerLedger(), Player(), Caps, _ => 1, out var refusal);

        Assert.Null(inventory);
        Assert.Equal(ActorInventory.MissingText, refusal);
    }

    [Fact]
    public void APartialReadRefusesAndIsNotCachedSoTheNextOpenReadsAgain()
    {
        var records = new Records();
        records.Partial.Add("npc1");
        var ledger = new ContainerLedger();

        var first = ActorInventory.Open("npc1", "base", records, ledger, Player(), Caps, _ => 1, out var refusal);

        Assert.Null(first);
        Assert.Equal(ActorInventory.PartialText, refusal);
        Assert.Null(ledger.Find("npc1"));

        records.Partial.Clear();
        var second = Open(records, ledger, Player());

        Assert.Equal(new[] { new InventoryStack(Gun, 1) }, ((IBarterLedger)second).VendorStacks);
        Assert.Equal(2, records.Reads);
    }

    [Fact]
    public void APartialReadOfOneActorDoesNotHideAnother()
    {
        var records = new Records();
        records.Partial.Add("npc1");
        var ledger = new ContainerLedger();

        Assert.Null(ActorInventory.Open("npc1", "base", records, ledger, Player(), Caps, _ => 1, out _));
        var other = ActorInventory.Open("npc2", "base", records, ledger, Player(), Caps, _ => 1, out var refusal);

        Assert.NotNull(other);
        Assert.Equal(string.Empty, refusal);
        Assert.Equal(new[] { "npc2" }, ledger.References);
    }

    [Fact]
    public void AFrozenActorKeepsItsStacksWhenALaterReadIsPartial()
    {
        var records = new Records();
        var ledger = new ContainerLedger();
        Open(records, ledger, Player());
        records.Partial.Add("npc1");

        var again = Open(records, ledger, Player());

        Assert.Equal(new[] { new InventoryStack(Gun, 1) }, ((IBarterLedger)again).VendorStacks);
    }

    [Fact]
    public void BuyMovesItemAndCapsBothWays()
    {
        var player = Player(new InventoryStack(Caps, 100));
        var inventory = Open(new Records(), new ContainerLedger(), player);

        var result = inventory.Commit([new BarterLine(BarterSide.Vendor, Gun, 1)], -30);

        Assert.True(result.Ok);
        Assert.Equal(new[] { new InventoryStack(Caps, 70), new InventoryStack(Gun, 1) }, player.Stacks);
        Assert.Equal(new[] { new InventoryStack(Caps, 80) }, inventory.ContainerStacks);
        Assert.Equal(1, inventory.Revision);
    }

    [Fact]
    public void SellMovesItemToVendorAndPaysPlayer()
    {
        var player = Player(new InventoryStack(Tea, 4));
        var inventory = Open(new Records(), new ContainerLedger(), player);

        var result = inventory.Commit([new BarterLine(BarterSide.Player, Tea, 3)], 12);

        Assert.True(result.Ok);
        Assert.Equal(new[] { new InventoryStack(Tea, 1), new InventoryStack(Caps, 12) }, player.Stacks);
        Assert.Equal(new[] { new InventoryStack(Gun, 1), new InventoryStack(Caps, 38), new InventoryStack(Tea, 3) }, inventory.ContainerStacks);
    }

    [Fact]
    public void FailedCommitChangesNothing()
    {
        var player = Player(new InventoryStack(Tea, 1), new InventoryStack(Caps, 5));
        var inventory = Open(new Records(), new ContainerLedger(), player);
        var playerBefore = player.Stacks.ToArray();
        var vendorBefore = inventory.ContainerStacks.ToArray();

        Assert.Equal(ActorInventory.NotEnoughText, inventory.Commit([new BarterLine(BarterSide.Player, Tea, 2)], 0).Reason);
        Assert.Equal(ActorInventory.NotEnoughText, inventory.Commit([new BarterLine(BarterSide.Vendor, Gun, 1), new BarterLine(BarterSide.Vendor, Gun, 1)], 0).Reason);
        Assert.Equal(ActorInventory.ShortCapsText, inventory.Commit([new BarterLine(BarterSide.Vendor, Gun, 1)], -6).Reason);
        Assert.Equal(ActorInventory.VendorShortText, inventory.Commit([new BarterLine(BarterSide.Player, Tea, 1)], 51).Reason);
        Assert.Equal(ActorInventory.CapsLineText, inventory.Commit([new BarterLine(BarterSide.Vendor, Caps, 1)], 0).Reason);

        Assert.Equal(playerBefore, player.Stacks);
        Assert.Equal(vendorBefore, inventory.ContainerStacks);
        Assert.Equal(0, inventory.Revision);
    }

    [Fact]
    public void CapsLineIsRefusedInAnyCaseAndMinValueNetIsShort()
    {
        var inventory = Open(new Records(), new ContainerLedger(), Player(new InventoryStack(Caps, 5)));

        Assert.Equal(ActorInventory.CapsLineText, inventory.Commit([new BarterLine(BarterSide.Vendor, "00000F:FO4.ESM", 1)], 0).Reason);
        Assert.Equal(ActorInventory.ShortCapsText, inventory.Commit([], int.MinValue).Reason);
    }

    [Fact]
    public void MixedBasketSwapsBothItemsAndNetsCaps()
    {
        var player = Player(new InventoryStack(Tea, 2), new InventoryStack(Caps, 10));
        var inventory = Open(new Records(), new ContainerLedger(), player);

        var result = inventory.Commit([new BarterLine(BarterSide.Vendor, Gun, 1), new BarterLine(BarterSide.Player, Tea, 2)], -4);

        Assert.True(result.Ok);
        Assert.Equal(new[] { new InventoryStack(Caps, 6), new InventoryStack(Gun, 1) }, player.Stacks);
        Assert.Equal(new[] { new InventoryStack(Caps, 54), new InventoryStack(Tea, 2) }, inventory.ContainerStacks);
    }

    [Fact]
    public void InvestIsRefused()
    {
        var inventory = Open(new Records(), new ContainerLedger(), Player());

        Assert.False(inventory.CanInvest);
        Assert.Equal(0, inventory.InvestAmount);
        Assert.Equal(new ActionResultShape(false, ActorInventory.NoInvestText), Shape(inventory.Invest()));
    }

    record ActionResultShape(bool Ok, string Reason);

    static ActionResultShape Shape(OpenCommonwealth.Services.Ui.Menus.ActionResult result) => new(result.Ok, result.Reason);

    [Fact]
    public void LivingActorCannotBeLootedButADeadOneCan()
    {
        var player = Player();
        var alive = new Records { Alive = true };
        var living = (IContainerInventory)Open(alive, new ContainerLedger(), player);
        Assert.Equal(ActorInventory.AliveText, living.Refusal(ContainerSide.Container, Gun, 1));
        Assert.Equal(TransferStatus.Refused, living.Transfer(ContainerSide.Container, Gun, 1).Status);

        var corpse = (IContainerInventory)Open(new Records(), new ContainerLedger(), player);
        Assert.Equal(string.Empty, corpse.Refusal(ContainerSide.Container, Gun, 1));
        Assert.Equal(TransferStatus.Done, corpse.Transfer(ContainerSide.Container, Gun, 1).Status);
        Assert.Equal(TransferStatus.Refused, corpse.Transfer(ContainerSide.Container, Gun, 1).Status);
        Assert.Equal(new[] { new InventoryStack(Gun, 1) }, player.Stacks);
        Assert.Equal(new[] { new InventoryStack(Caps, 50) }, corpse.ContainerStacks);
    }

    [Fact]
    public void ActorStockSurvivesSaveAndLoad()
    {
        var ledger = new ContainerLedger();
        var player = Player(new InventoryStack(Caps, 100));
        Open(new Records(), ledger, player).Commit([new BarterLine(BarterSide.Vendor, Gun, 1)], -30);
        var sections = ContainerStateSave.Sections(ledger, player);

        var loadedLedger = new ContainerLedger();
        var loadedPlayer = new PlayerItems();
        ContainerStateSave.Apply(sections, loadedLedger, loadedPlayer);
        var reopened = Open(new Records(), loadedLedger, loadedPlayer);

        Assert.Equal(new[] { new InventoryStack(Caps, 80) }, reopened.ContainerStacks);
        Assert.Equal(new[] { new InventoryStack(Caps, 70), new InventoryStack(Gun, 1) }, loadedPlayer.Stacks);
    }

    [Fact]
    public void TradeOutcomeOpensBarterOnlyWhenAChestIsKnown()
    {
        Assert.Equal(new NpcTradeDecision(TradeRoute.Barter, "chest:a.esm"),
            NpcTradeRoute.Decide(DialogueOutcome.Trade, new Chests("chest:a.esm"), "npc1", "base"));
        Assert.Equal(new NpcTradeDecision(TradeRoute.NoVendorChest),
            NpcTradeRoute.Decide(DialogueOutcome.Trade, new Chests(null), "npc1", "base"));
        Assert.Equal(new NpcTradeDecision(TradeRoute.NoVendorChest),
            NpcTradeRoute.Decide(DialogueOutcome.Trade, new NoVendorChests(), "npc1", "base"));
        Assert.Equal(new NpcTradeDecision(TradeRoute.NotTrade),
            NpcTradeRoute.Decide(DialogueOutcome.Left, new Chests("chest:a.esm"), "npc1", "base"));
    }

    [Fact]
    public void A_companions_stock_moves_both_ways_and_survives_save_and_load()
    {
        var ledger = new ContainerLedger();
        var player = Player(new InventoryStack(Tea, 2));
        var companion = (IContainerInventory)Open(new Records(), ledger, player);

        Assert.Equal(TransferStatus.Done, companion.Transfer(ContainerSide.Player, Tea, 2).Status);
        Assert.Equal(TransferStatus.Done, companion.Transfer(ContainerSide.Container, Gun, 1).Status);
        var loadedLedger = new ContainerLedger();
        var loadedPlayer = new PlayerItems();
        ContainerStateSave.Apply(ContainerStateSave.Sections(ledger, player), loadedLedger, loadedPlayer);
        var reopened = Open(new Records(), loadedLedger, loadedPlayer);

        Assert.Equal(new[] { new InventoryStack(Gun, 1) }, loadedPlayer.Stacks);
        Assert.Equal(new[] { new InventoryStack(Caps, 50), new InventoryStack(Tea, 2) }, reopened.ContainerStacks.OrderBy(s => s.Id, System.StringComparer.Ordinal));
    }
}
