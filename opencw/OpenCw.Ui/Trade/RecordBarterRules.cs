using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed class RecordBarterRules : IBarterRules
{
    public const string NoPriceText = "No price is known for this item.";
    public const string BadCountText = "Each line needs at least one item.";
    public const string TooLargeText = "The total is too large.";
    public const string MissingHeader = "";
    public const int MaxUnitPrice = 10_000_000;

    private readonly IBarterLedger _ledger;
    private readonly IItemRecords _items;
    private readonly Func<string?>? _playerListHeader;
    private readonly Func<string?>? _vendorListHeader;

    public RecordBarterRules(IBarterLedger ledger, IItemRecords items, Func<string?>? playerListHeader = null,
        Func<string?>? vendorListHeader = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(items);
        _ledger = ledger;
        _items = items;
        _playerListHeader = playerListHeader;
        _vendorListHeader = vendorListHeader;
    }

    public string VendorName => _ledger.VendorName;

    public string PlayerListHeader => _playerListHeader?.Invoke() is { Length: > 0 } header ? header : MissingHeader;

    public string VendorListHeader => _vendorListHeader?.Invoke() is { Length: > 0 } header ? header : MissingHeader;

    public int Revision => _ledger.Revision;

    public int PlayerCaps => _ledger.PlayerCaps;

    public int VendorCaps => _ledger.VendorCaps;

    public IReadOnlyList<BarterStack> PlayerStacks => _ledger.PlayerStacks.Select(Describe).ToArray();

    public IReadOnlyList<BarterStack> VendorStacks => _ledger.VendorStacks.Select(Describe).ToArray();

    public bool CanInvest => _ledger.CanInvest;

    public int InvestAmount => _ledger.InvestAmount;

    public int UnitPrice(BarterSide side, string itemId)
    {
        var value = _items.Find(itemId)?.Value ?? 0;
        var factor = _ledger.PriceFactor(side);
        if (value <= 0 || !double.IsFinite(factor) || factor <= 0)
            return 0;
        return (int)Math.Clamp(Math.Round(value * factor, MidpointRounding.AwayFromZero), 1, MaxUnitPrice);
    }

    public string Refusal(BarterSide side, string itemId)
    {
        var reason = _ledger.Refusal(side, itemId);
        if (reason.Length > 0)
            return reason;
        return UnitPrice(side, itemId) > 0 ? string.Empty : NoPriceText;
    }

    public BarterQuote Quote(IReadOnlyList<BarterLine> basket)
    {
        if (basket.Any(line => line.Count <= 0))
            return new BarterQuote(0, false, BadCountText);
        var prices = basket.Select(line => UnitPrice(line.Side, line.ItemId)).ToArray();
        if (prices.Any(price => price <= 0))
            return new BarterQuote(0, false, NoPriceText);
        long total;
        try
        {
            total = basket.Select((line, index) => checked((long)prices[index] * line.Count * (line.Side == BarterSide.Player ? 1 : -1))).Sum();
        }
        catch (OverflowException)
        {
            return new BarterQuote(0, false, TooLargeText);
        }
        if (Math.Abs(total) > int.MaxValue)
            return new BarterQuote(0, false, TooLargeText);
        var net = (int)total;
        if (net < 0 && PlayerCaps < -net)
            return new BarterQuote(net, false, "Not enough caps.");
        if (net > 0 && VendorCaps < net)
            return new BarterQuote(net, false, "The vendor is short of caps.");
        return new BarterQuote(net, true);
    }

    public ActionResult Commit(IReadOnlyList<BarterLine> basket)
    {
        foreach (var line in basket)
            if (Refusal(line.Side, line.ItemId) is { Length: > 0 } reason)
                return new ActionResult(false, reason);
        var quote = Quote(basket);
        return quote.CanCommit ? _ledger.Commit(basket, quote.PlayerNet) : new ActionResult(false, quote.Refusal);
    }

    public ActionResult Invest() => _ledger.Invest();

    private BarterStack Describe(InventoryStack stack) =>
        new(stack.Id, _items.Find(stack.Id)?.Name ?? stack.Id, stack.Count);
}
