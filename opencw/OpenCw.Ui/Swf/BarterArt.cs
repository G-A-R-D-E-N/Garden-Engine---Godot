using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class BarterArt
{
    public const string Movie = "Interface/BarterMenu.swf";
    private const string M = "FilterHolder_mc/Menu_mc/";

    public static SwfBindings Build(BarterController controller, IBarterRules rules)
    {
        var b = new SwfBindings();
        b.Hide.Add(M + "QuantityMenu_mc");
        b.Hide.Add(M + "ContainerInventory_mc/ContainerBracketBackground_mc");
        b.Hide.Add(M + "PlayerInventory_mc/PlayerBracketBackground_mc");
        foreach (var field in StrayFields)
            b.Hide.Add(M + field);
        b.Text[M + "PlayerInventory_mc/PlayerCaps_tf"] = SwfBindings.Number(rules.PlayerCaps);
        b.Text[M + "ContainerInventory_mc/VendorCaps_tf"] = SwfBindings.Number(rules.VendorCaps);
        b.Text[M + "CapsTransferInfo_mc/TransferCaps_tf"] = Math.Abs((long)(controller.Quote?.PlayerNet ?? 0)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        b.Text[M + "PlayerInventory_mc/PlayerListHeader/textField"] = rules.PlayerListHeader;
        b.Text[M + "ContainerInventory_mc/ContainerListHeader/textField"] = rules.VendorName.ToUpperInvariant();
        var selected = controller.Rows.Selected;
        BindList(b, "PlayerInventory_mc/PlayerListHeader", "PlayerInventory_mc/PlayerList_mc/", Rows(rules.PlayerStacks, BarterSide.Player, selected));
        BindList(b, "ContainerInventory_mc/ContainerListHeader", "ContainerList_mc/", Rows(rules.VendorStacks, BarterSide.Vendor, selected));
        return b;
    }

    private static readonly string[] StrayFields =
    {
        "PlayerInventory_mc/PlayerSwitchButton_tf", "PlayerInventory_mc/LeftHitBox_tf", "PlayerInventory_mc/RightHitBox_tf",
        "ContainerInventory_mc/ContainerSwitchButton_tf", "ContainerInventory_mc/LeftHitBox_tf", "ContainerInventory_mc/RightHitBox_tf",
    };

    private static void BindList(SwfBindings b, string header, string list, List<SwfListRow> rows)
    {
        var window = SwfBindings.Window(rows);
        b.Lists[M + header] = window.Rows;
        b.Headers.Add(M + header);
        b.Hide.Add(M + list + "border");
        if (!window.MoreAbove)
            b.Hide.Add(M + list + "ScrollUp");
        if (!window.MoreBelow)
            b.Hide.Add(M + list + "ScrollDown");
    }

    private static List<SwfListRow> Rows(IReadOnlyList<BarterStack> stacks, BarterSide side, BarterRow? selected) =>
        stacks.Select(s => new SwfListRow(s.Name + (s.Count > 1 ? " (" + s.Count + ")" : ""),
            selected is not null && selected.Side == side && selected.Id == s.Id)).ToList();
}
