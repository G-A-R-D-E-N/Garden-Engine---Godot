using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class BarterArt
{
    public const string Movie = "Interface/BarterMenu.swf";
    private const string M = "FilterHolder_mc/Menu_mc/";
    public const double PlayerRight = 319.77;
    public const double VendorRight = 321.14;
    public const double Bottom = 353.26;
    public const double HintTop = 688;
    public const string PlayerAnchor = M + "PlayerInventory_mc/PlayerListHeader";
    public const string VendorAnchor = M + "ContainerInventory_mc/ContainerListHeader";

    public static readonly SwfReadOptions Read = new(true, Array.Empty<string>());

    public static SwfBindings Build(BarterController controller, IBarterRules rules, SwfScroll? scroll = null, bool cardArt = true)
    {
        var b = new SwfBindings();
        b.Hide.Add(M + "QuantityMenu_mc");
        foreach (var field in StrayFields)
            b.Hide.Add(M + field);
        b.Text[M + "PlayerInventory_mc/PlayerCaps_tf"] = SwfBindings.Number(rules.PlayerCaps);
        b.Text[M + "ContainerInventory_mc/VendorCaps_tf"] = SwfBindings.Number(rules.VendorCaps);
        b.Text[M + "CapsTransferInfo_mc/TransferCaps_tf"] = Math.Abs((long)(controller.Quote?.PlayerNet ?? 0)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        b.Text[M + "PlayerInventory_mc/PlayerListHeader/textField"] = rules.PlayerListHeader;
        b.Text[M + "ContainerInventory_mc/ContainerListHeader/textField"] = rules.VendorName.ToUpperInvariant();
        var selected = controller.Rows.Selected;
        if (cardArt)
            ItemCardArt.Add(b, controller.CardEntries);
        BindList(b, PlayerAnchor, "PlayerInventory_mc/PlayerList_mc/", Rows(rules.PlayerStacks, BarterSide.Player, selected), scroll, controller.Side == BarterSide.Player, PlayerRight);
        BindList(b, VendorAnchor, "ContainerList_mc/", Rows(rules.VendorStacks, BarterSide.Vendor, selected), scroll, controller.Side == BarterSide.Vendor, VendorRight);
        return b;
    }

    public static List<MenuTextLine> CardLines(BarterController controller, bool cardArt = true)
    {
        var lines = (cardArt ? controller.CardNotes : controller.PlainNotes).ToList();
        if (controller.Message.Length > 0)
            lines.Add(new MenuTextLine(controller.Message, LineTone.Bad));
        return lines;
    }

    private static readonly string[] StrayFields =
    {
        "PlayerInventory_mc/PlayerSwitchButton_tf", "PlayerInventory_mc/LeftHitBox_tf", "PlayerInventory_mc/RightHitBox_tf",
        "ContainerInventory_mc/ContainerSwitchButton_tf", "ContainerInventory_mc/LeftHitBox_tf", "ContainerInventory_mc/RightHitBox_tf",
    };

    private static void BindList(SwfBindings b, string header, string list, List<SwfListRow> rows, SwfScroll? scroll, bool active, double right)
    {
        var window = SwfScroll.Window(scroll, header, rows);
        b.Lists[header] = window.Rows;
        b.Headers.Add(header);
        if (!active)
        {
            b.Hide.Add(header + "/LeftArrow");
            b.Hide.Add(header + "/RightArrow");
        }
        b.Brackets.Add(new SwfBracket(header[..header.LastIndexOf('/')], header, right, Bottom));
        b.Hide.Add(M + list + "border");
        if (!window.MoreAbove)
            b.Hide.Add(M + list + "ScrollUp");
        if (!window.MoreBelow)
            b.Hide.Add(M + list + "ScrollDown");
    }

    private static List<SwfListRow> Rows(IReadOnlyList<BarterStack> stacks, BarterSide side, BarterRow? selected) =>
        stacks.Where(s => !string.IsNullOrWhiteSpace(s.Id) && s.Count > 0).Select(s => new SwfListRow(s.Name + (s.Count > 1 ? " (" + s.Count + ")" : ""),
            selected is not null && selected.Side == side && selected.Id == s.Id)).ToList();
}
