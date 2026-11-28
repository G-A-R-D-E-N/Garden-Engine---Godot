using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class ContainerArt
{
    public const string Movie = "Interface/ContainerMenu.swf";
    public const string M = "FilterHolder_mc/Menu_mc/";
    public const string PlayerAnchor = M + "PlayerInventory_mc/PlayerListHeader";
    public const string ContainerAnchor = M + "ContainerInventory_mc/ContainerListHeader";
    public const double ScrollbarScale = 1.958;
    public const double TrackX = 1;
    public const double TrackWidth = 62;
    public const double ThumbWidth = 3.03;
    public const double ThumbDesignX = 51;

    public static readonly SwfReadOptions Read = new(true, new[] { M + "QuantityMenu_mc" });

    private const string Quantity = M + "QuantityMenu_mc";
    public const double PlayerRight = 321.105;
    public const double ContainerRight = 321.11;
    public const double Bottom = 353.25;
    public const double HintTop = 655;

    private static readonly string[] Strays =
    {
        "PlayerInventory_mc/PlayerSwitchButton_tf", "PlayerInventory_mc/LeftHitBox_tf", "PlayerInventory_mc/RightHitBox_tf",
        "ContainerInventory_mc/ContainerSwitchButton_tf", "ContainerInventory_mc/LeftHitBox_tf", "ContainerInventory_mc/RightHitBox_tf",
        "PlayerInventory_mc/PlayerCaps_tf", "PlayerInventory_mc/#109", "PlayerInventory_mc/#20",
        "PickpocketInfo_mc", "ButtonHintBar_mc",
    };

    public static string AnchorOf(ContainerSide side) => side == ContainerSide.Player ? PlayerAnchor : ContainerAnchor;

    public static SwfBindings Build(ContainerController c, SwfScroll? scroll = null, bool cardArt = true)
    {
        var b = new SwfBindings();
        foreach (var path in Strays)
            b.Hide.Add(M + path);
        b.Text[M + "PlayerInventory_mc/PlayerListHeader/textField"] = c.PlayerListHeader;
        b.Text[M + "ContainerInventory_mc/ContainerListHeader/textField"] = c.Title.ToUpperInvariant();
        b.Text[M + "PlayerInventory_mc/PlayerWeight_tf"] = Weight(c);
        if (cardArt && c.Pick is null)
            ItemCardArt.Add(b, c.CardEntries);
        BindList(b, c, ContainerSide.Container, "ContainerList_mc/", scroll);
        BindList(b, c, ContainerSide.Player, "PlayerInventory_mc/PlayerList_mc/", scroll);
        if (c.Pick is { } pick)
        {
            b.Text[Quantity + "/Label_tf"] = "QUANTITY";
            b.Text[Quantity + "/Value_tf"] = SwfBindings.Number(pick.Quantity);
            b.Place[Quantity + "/QuantityScrollbar_mc/Thumb_mc"] = new SwfPlace(1, ThumbDelta(pick.Quantity, pick.Max), 0);
        }
        else
            b.Hide.Add(Quantity);
        return b;
    }

    public static double ThumbDelta(int quantity, int max)
    {
        var value = max > 0 ? Math.Clamp(quantity, 0, max) / (double)max : 0;
        return (TrackX + value * (TrackWidth - ThumbWidth) - ThumbDesignX) * ScrollbarScale;
    }

    public static SwfListWindow WindowOf(ContainerController c, ContainerSide side, SwfScroll? scroll = null) =>
        SwfScroll.Window(scroll, AnchorOf(side), Rows(c, side));

    public static List<MenuTextLine> CardLines(ContainerController c, bool cardArt = true)
    {
        var lines = (cardArt ? c.CardNotes : c.PlainNotes).ToList();
        if (c.Message.Length > 0)
            lines.Add(new MenuTextLine(c.Message, LineTone.Bad));
        return lines;
    }

    private static string Weight(ContainerController c) =>
        c.CarryLimit > 0 ? NumberText.Format(c.PlayerWeight) + "/" + NumberText.Format(c.CarryLimit) : NumberText.Format(c.PlayerWeight);

    private static List<SwfListRow> Rows(ContainerController c, ContainerSide side)
    {
        var selected = c.Rows.Selected;
        return c.RowsOf(side).Select(r => new SwfListRow(ContainerController.TextOf(r),
            selected is not null && selected.Side == side && selected.Id == r.Id)).ToList();
    }

    private static void BindList(SwfBindings b, ContainerController c, ContainerSide side, string list, SwfScroll? scroll)
    {
        var window = WindowOf(c, side, scroll);
        var anchor = AnchorOf(side);
        b.Lists[anchor] = window.Rows;
        b.Headers.Add(anchor);
        if (c.Side != side)
        {
            b.Hide.Add(anchor + "/LeftArrow");
            b.Hide.Add(anchor + "/RightArrow");
        }
        b.Brackets.Add(side == ContainerSide.Player ? new SwfBracket(M + "PlayerInventory_mc", anchor, PlayerRight, Bottom) : new SwfBracket(M + "ContainerInventory_mc", anchor, ContainerRight, Bottom));
        b.Hide.Add(M + list + "border");
        if (!window.MoreAbove)
            b.Hide.Add(M + list + "ScrollUp");
        if (!window.MoreBelow)
            b.Hide.Add(M + list + "ScrollDown");
    }
}
