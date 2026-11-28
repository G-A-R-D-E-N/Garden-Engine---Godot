using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class PipboyInvArt
{
    public const string Movie = "Interface/Pipboy_InvPage.swf";

    private const string Info = "Menu_mc/BottomBar_mc/Info_mc/";
    private const string Header = "Menu_mc/Header_mc/";
    private const string Carousel = Header + "TabHeader_mc/AlphaHolder/";
    private const string ListPath = "List_mc";

    public static readonly string[] FooterFields = { "HP_tf", "LVL_tf", "AP_tf" };

    private static readonly (string Slot, int Offset)[] TabSlots =
    {
        ("Selected", 0), ("RightOne", 1), ("LeftOne", -1), ("RightTwo", 2), ("LeftTwo", -2),
    };

    private static readonly string[] Templates = { "ComponentList_mc", "ComponentOwnersList_mc" };

    public static SwfBindings Shell(InventoryListView view, IPipBoyStatusSource? source)
    {
        var b = new SwfBindings();
        var segments = PipBoyFooter.Segments(PipBoyTab.Inventory, source);
        for (var i = 0; i < FooterFields.Length; i++)
        {
            if (i < segments.Count)
                b.Text[Info + FooterFields[i]] = segments[i];
            else
                b.Hide.Add(Info + FooterFields[i]);
        }
        b.Hide.Add(Info + "XPMeter_mc");
        b.Hide.Add(Header + "LeftTriggerButton_mc");
        b.Hide.Add(Header + "RightTriggerButton_mc");
        BindTabs(b, view);
        return b;
    }

    public static SwfBindings Page(InventoryListView view, SwfScroll? scroll = null)
    {
        var b = new SwfBindings();
        foreach (var template in Templates)
            b.Hide.Add(template);
        var rows = new List<SwfListRow>();
        for (var i = 0; i < view.RowCount; i++)
        {
            var detail = view.RowDetail(i);
            rows.Add(new SwfListRow(detail.Length > 0 ? view.RowText(i) + "  " + detail : view.RowText(i), i == view.SelectedIndex));
        }
        var window = SwfScroll.Window(scroll, ListPath, rows);
        b.Lists[ListPath] = window.Rows;
        b.Hide.Add(ListPath + "/border");
        if (!window.MoreAbove)
            b.Hide.Add(ListPath + "/ScrollUp");
        if (!window.MoreBelow)
            b.Hide.Add(ListPath + "/ScrollDown");
        return b;
    }

    private static void BindTabs(SwfBindings b, InventoryListView view)
    {
        var tabs = view.Tabs;
        if (tabs.Count == 0)
            return;
        var shown = new HashSet<int>();
        foreach (var (slot, offset) in TabSlots)
        {
            var index = ((view.TabIndex + offset) % tabs.Count + tabs.Count) % tabs.Count;
            if (shown.Add(index))
                b.Text[Carousel + slot + "/textField_tf"] = tabs[index].ToUpperInvariant();
            else
                b.Hide.Add(Carousel + slot);
        }
    }
}
