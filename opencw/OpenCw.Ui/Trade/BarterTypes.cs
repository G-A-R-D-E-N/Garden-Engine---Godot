using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Trade;

public enum BarterSide
{
    Player,
    Vendor,
}

public sealed record BarterStack(string Id, string Name, int Count);

public sealed record BarterLine(BarterSide Side, string ItemId, int Count);

public sealed record BarterQuote(int PlayerNet, bool CanCommit, string Refusal = "");

public interface IBarterRules
{
    string VendorName { get; }

    string PlayerListHeader { get; }

    string VendorListHeader { get; }

    int Revision { get; }

    int PlayerCaps { get; }

    int VendorCaps { get; }

    IReadOnlyList<BarterStack> PlayerStacks { get; }

    IReadOnlyList<BarterStack> VendorStacks { get; }

    bool CanInvest { get; }

    int InvestAmount { get; }

    int UnitPrice(BarterSide side, string itemId);

    string Refusal(BarterSide side, string itemId);

    BarterQuote Quote(IReadOnlyList<BarterLine> basket);

    ActionResult Commit(IReadOnlyList<BarterLine> basket);

    ActionResult Invest();
}
