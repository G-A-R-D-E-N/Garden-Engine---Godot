using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public interface IContainerInventory
{
    ContainerMode Mode { get; }

    string Title { get; }

    double CarryLimit { get; }

    int Revision { get; }

    IReadOnlyList<InventoryStack> ContainerStacks { get; }

    IReadOnlyList<InventoryStack> PlayerStacks { get; }

    string Refusal(ContainerSide from, string itemId, int count);

    int Chance(ContainerSide from, string itemId);

    int Chance(ContainerSide from, string itemId, int count) => Chance(from, itemId);

    ContainerTransferResult Transfer(ContainerSide from, string itemId, int count);
}

public interface IBarterLedger
{
    string VendorName { get; }

    int Revision { get; }

    int PlayerCaps { get; }

    int VendorCaps { get; }

    IReadOnlyList<InventoryStack> PlayerStacks { get; }

    IReadOnlyList<InventoryStack> VendorStacks { get; }

    bool CanInvest { get; }

    int InvestAmount { get; }

    double PriceFactor(BarterSide side);

    string Refusal(BarterSide side, string itemId);

    ActionResult Commit(IReadOnlyList<BarterLine> basket, int playerNet);

    ActionResult Invest();
}
