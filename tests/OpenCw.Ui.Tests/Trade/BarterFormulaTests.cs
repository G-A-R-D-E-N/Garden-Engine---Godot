using System;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BarterFormulaTests
{
    const string Caps = "00000f:fo4.esm";
    const string Gun = "000100:a.esm";
    const string Tea = "000101:a.esm";
    const string Chest = "000900:a.esm";

    static readonly BarterSettings Esm = new(Max: 3, Min: 2, BuyMax: 1.2f, SellMax: 0.8f);

    static void Near(float expected, float actual) => Assert.InRange(actual, expected - 1e-5f, expected + 1e-5f);

    [Theory]
    [InlineData(0f, 3f, 1f / 3f)]
    [InlineData(5f, 2.5f, 0.4f)]
    [InlineData(10f, 2f, 0.5f)]
    public void The_multiplier_interpolates_from_max_to_min_with_charisma_and_the_sell_side_is_the_inverse(float charisma, float buy, float sell)
    {
        Near(buy, BarterFormula.Multiplier(Esm, charisma, false));
        Near(sell, BarterFormula.Multiplier(Esm, charisma, true));
    }

    [Fact]
    public void Buying_never_costs_less_than_the_value_and_is_floored_at_the_buy_setting()
    {
        Near(1f, BarterFormula.Multiplier(new BarterSettings(0.5f, 0.5f, 0.5f, 0.95f), 5, false));
        Near(1.5f, BarterFormula.Multiplier(new BarterSettings(1.1f, 1.1f, 1.5f, 0.95f), 5, false));
        Near(1.1f, BarterFormula.Multiplier(new BarterSettings(1.1f, 1.1f, 1.05f, 0.95f), 5, false));
    }

    [Fact]
    public void Selling_is_capped_at_the_sell_setting()
    {
        Near(0.5f, BarterFormula.Multiplier(new BarterSettings(1.25f, 1.25f, 1.05f, 0.5f), 5, true));
        Near(0.8f, BarterFormula.Multiplier(new BarterSettings(1.25f, 1.25f, 1.05f, 0.95f), 5, true));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0f)]
    [InlineData(-2f)]
    public void A_sell_inverse_outside_zero_to_one_resets_to_one_before_the_cap(float margin)
    {
        var settings = new BarterSettings(margin, margin, 1.05f, 2f);

        Near(1f, BarterFormula.Multiplier(settings, 5, true));
        Near(0.95f, BarterFormula.Multiplier(settings with { SellMax = 0.95f }, 5, true));
    }

    [Fact]
    public void Read_takes_the_four_settings_from_the_GMST_records_and_defaults_the_rest()
    {
        var reader = new FakeRecordReader();
        Gmst(reader, "000001:fo4.esm", "fBarterMax", "3.5");
        Gmst(reader, "000002:fo4.esm", "fBarterMin", "2");
        Gmst(reader, "000003:fo4.esm", "fBarterBuyMax", "1.2");
        Gmst(reader, "000004:fo4.esm", "fBarterSellMax", "not a number");

        Assert.Equal(new BarterSettings(3.5f, 2f, 1.2f, 0.95f), BarterSettings.Read(reader));
        Assert.Equal(new BarterSettings(2.5f, 1.5f, 1.05f, 0.95f), BarterSettings.Read(new FakeRecordReader()));
    }

    static void Gmst(FakeRecordReader reader, string id, string editorId, string value) =>
        reader.Add(id, "GMST", editorId).Scalars[PickpocketSettings.FloatPath] = value;

    [Fact]
    public void The_player_side_sells_and_the_vendor_side_buys_and_an_unknown_charisma_keeps_one_to_one()
    {
        var prices = BarterPricing.For(Esm, 10);

        Assert.Equal(0.5, prices(BarterSide.Player), 4);
        Assert.Equal(2.0, prices(BarterSide.Vendor), 4);
        Assert.Equal(1.0, BarterPricing.For(Esm, null)(BarterSide.Vendor));
        Assert.Equal(1.0, BarterPricing.For(Esm, float.NaN)(BarterSide.Player));
        Assert.Equal(1.0, BarterPricing.For(Esm, float.PositiveInfinity)(BarterSide.Player));
    }

    sealed class Actors : IActorRecords
    {
        public ActorSetup? Read(string refKey, string baseKey) => new("Vic", true, []);
    }

    sealed class Chests : IContainerRecords
    {
        public ContainerSetup? Read(string refKey, string baseKey) => new("Chest", false, false, [new(Gun, 2), new(Caps, 100)]);
    }

    sealed class Items : IItemRecords
    {
        public ItemRecord? Find(string id) => id switch
        {
            Gun => new ItemRecord(id, "WEAP", "Gun", 30, 1),
            Tea => new ItemRecord(id, "ALCH", "Tea", 5, 0.1),
            _ => null,
        };
    }

    [Fact]
    public void A_vendor_trade_prices_both_directions_by_the_formula_and_moves_the_net_caps()
    {
        var ledger = new ContainerLedger();
        var player = new PlayerItems();
        player.Replace([new InventoryStack(Caps, 100), new InventoryStack(Tea, 4)]);
        var inventory = ActorInventory.OpenVendor("npc1", "base", Chest, "chestbase", new Actors(), new Chests(), ledger, player, Caps, BarterPricing.For(Esm, 10), out var refusal)!;
        var rules = new RecordBarterRules(inventory, new Items());

        Assert.Equal(string.Empty, refusal);
        Assert.Equal(60, rules.UnitPrice(BarterSide.Vendor, Gun));
        Assert.Equal(3, rules.UnitPrice(BarterSide.Player, Tea));

        var basket = new BarterLine[] { new(BarterSide.Vendor, Gun, 1), new(BarterSide.Player, Tea, 4) };
        Assert.Equal(-48, rules.Quote(basket).PlayerNet);
        Assert.True(rules.Commit(basket).Ok);

        Assert.Equal(52, player.CountOf(Caps));
        Assert.Equal(1, player.CountOf(Gun));
        Assert.Equal(0, player.CountOf(Tea));
        Assert.Equal(148, ledger.Find(Chest)!.CountOf(Caps));
        Assert.Equal(4, ledger.Find(Chest)!.CountOf(Tea));
    }

    [Fact]
    public void A_dearer_buy_price_is_refused_when_the_player_cannot_cover_it()
    {
        var player = new PlayerItems();
        player.Replace([new InventoryStack(Caps, 59)]);
        var inventory = ActorInventory.OpenVendor("npc1", "base", Chest, "chestbase", new Actors(), new Chests(), new ContainerLedger(), player, Caps, BarterPricing.For(Esm, 10), out _)!;
        var rules = new RecordBarterRules(inventory, new Items());

        Assert.False(rules.Quote([new(BarterSide.Vendor, Gun, 1)]).CanCommit);
        Assert.False(rules.Commit([new(BarterSide.Vendor, Gun, 1)]).Ok);
        Assert.Equal(59, player.CountOf(Caps));
    }
}
