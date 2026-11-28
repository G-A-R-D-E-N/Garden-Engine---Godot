using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Swf;

public interface ISwfMetrics
{
    bool Origin(string path, out double x, out double y);

    bool TextSpan(string path, string text, out double left, out double right);
}

public static class PipboyShell
{
    public const double SelectorGap = 12.5;
    public const double TabSpacing = 12.5;
    public const double LineThickness = 3;

    private const string Info = "Menu_mc/BottomBar_mc/Info_mc/";
    private const string Header = "Menu_mc/Header_mc/";
    private const string PageHeader = Header + "PageHeader_mc";
    private const string TabHeader = Header + "TabHeader_mc";
    private const string Carousel = TabHeader + "/AlphaHolder/";

    public static readonly string[] FooterFields = { "HP_tf", "LVL_tf", "AP_tf" };

    public static readonly string[] Required =
    {
        Info + "HP_tf", Info + "LVL_tf", Info + "AP_tf", Info + "XPMeter_mc", Info + "XPMeter_mc/Fill_mc",
        PageHeader + "/STAT_tf", PageHeader + "/INV_tf", PageHeader + "/DATA_tf", PageHeader + "/MAP_tf", PageHeader + "/RADIO_tf",
        PageHeader + "/Selector_Left", PageHeader + "/Selector_Right", PageHeader + "/LeftBorder", PageHeader + "/RightBorder",
        Carousel + "Selected/textField_tf", Carousel + "LeftOne", Carousel + "RightOne", Carousel + "LeftTwo", Carousel + "RightTwo",
    };

    private static readonly (PipBoyTab Tab, string Field, string Label)[] Pages =
    {
        (PipBoyTab.Stats, "STAT_tf", "STAT"),
        (PipBoyTab.Inventory, "INV_tf", "INV"),
        (PipBoyTab.Data, "DATA_tf", "DATA"),
        (PipBoyTab.Map, "MAP_tf", "MAP"),
        (PipBoyTab.Radio, "RADIO_tf", "RADIO"),
    };

    public static readonly (string Slot, int Offset)[] Slots =
    {
        ("Selected", 0), ("LeftOne", -1), ("RightOne", 1), ("LeftTwo", -2), ("RightTwo", 2),
    };

    public static SwfBindings Bind(PipBoyTab tab, IReadOnlyCollection<PipBoyTab> pages, IListMenuView? list, IPipBoyStatusSource? source, ISwfMetrics? metrics)
    {
        var b = new SwfBindings();
        BindFooter(b, tab, source);
        b.Hide.Add(Header + "LeftTriggerButton_mc");
        b.Hide.Add(Header + "RightTriggerButton_mc");
        b.Hide.Add("Menu_mc/ButtonHintBar_mc");
        var span = BindPages(b, tab, pages, metrics);
        BindTabs(b, list, span, metrics);
        return b;
    }

    private static void BindFooter(SwfBindings b, PipBoyTab tab, IPipBoyStatusSource? source)
    {
        var segments = PipBoyFooter.Segments(tab, source).ToList();
        var meter = tab == PipBoyTab.Stats && source?.Level is not null && source.LevelProgress is { } fill && double.IsFinite(fill);
        if (segments.Count > 1)
            segments[1] = LevelText(segments[1], meter);
        for (var i = 0; i < FooterFields.Length; i++)
        {
            if (i < segments.Count)
                b.Text[Info + FooterFields[i]] = segments[i];
            else
                b.Hide.Add(Info + FooterFields[i]);
        }
        if (meter)
            b.FillX[Info + "XPMeter_mc/Fill_mc"] = source!.LevelProgress!.Value;
        else
            b.Hide.Add(Info + "XPMeter_mc");
    }

    // Invariant: the drawn XP meter already shows the progress, so the level text drops its percentage while the meter is bound.
    internal static string LevelText(string segment, bool meter) =>
        meter && segment.EndsWith('%') && segment.LastIndexOf(' ') is var cut and > 0 ? segment[..cut] : segment;

    public static string PagePath(PipBoyTab tab) => PageHeader + "/" + Pages.First(p => p.Tab == tab).Field;

    public static string SlotPath(string slot) => Carousel + slot + "/textField_tf";

    private static (double Left, double Right)? BindPages(SwfBindings b, PipBoyTab current, IReadOnlyCollection<PipBoyTab> pages, ISwfMetrics? metrics)
    {
        (double, double)? span = null;
        foreach (var (tab, field, label) in Pages)
        {
            var path = PageHeader + "/" + field;
            if (!pages.Contains(tab))
            {
                b.Hide.Add(path);
                continue;
            }
            b.Text[path] = label;
            if (tab == current && metrics is not null && metrics.TextSpan(path, label, out var left, out var right))
                span = (left, right);
        }
        if (span is not { } found || metrics is null)
            return null;
        PlaceSelector(b, metrics, PageHeader + "/Selector_Left", PageHeader + "/LeftBorder", found.Item1 - SelectorGap);
        PlaceSelector(b, metrics, PageHeader + "/Selector_Right", PageHeader + "/RightBorder", found.Item2 + SelectorGap);
        return found;
    }

    private static void PlaceSelector(SwfBindings b, ISwfMetrics metrics, string selector, string border, double x)
    {
        if (!metrics.Origin(selector, out var sx, out _) || !metrics.Origin(border, out var bx, out var by))
            return;
        b.Place[selector] = new SwfPlace(1, x - sx, 0);
        b.Sprites.Add(new SwfSprite("", PageHeader, Math.Min(bx, x), by - LineThickness / 2, 1, Bar: new SwfSpriteBar(0, 0, Math.Abs(x - bx), LineThickness, 1)));
    }

    private static void BindTabs(SwfBindings b, IListMenuView? list, (double Left, double Right)? pageSpan, ISwfMetrics? metrics)
    {
        var tabs = list?.Tabs ?? [];
        if (list is null || tabs.Count <= 1)
        {
            b.Hide.Add(TabHeader);
            return;
        }
        var widths = new Dictionary<string, double>();
        foreach (var (slot, offset) in Slots)
        {
            var index = list.TabIndex + offset;
            var path = Carousel + slot;
            if (index < 0 || index >= tabs.Count)
            {
                b.Hide.Add(path);
                continue;
            }
            var text = tabs[index].ToUpperInvariant();
            b.Text[path + "/textField_tf"] = text;
            if (metrics is not null && metrics.TextSpan(path + "/textField_tf", text, out var left, out var right))
                widths[slot] = right - left;
        }
        if (metrics is null || pageSpan is not { } page)
            return;
        if (metrics.Origin(TabHeader, out var headerX, out _))
            b.Place[TabHeader] = new SwfPlace(1, (page.Left + page.Right) / 2 - headerX, 0);
        PlaceSlot(b, metrics, widths, "LeftOne", "Selected", -1);
        PlaceSlot(b, metrics, widths, "LeftTwo", "LeftOne", -1);
        PlaceSlot(b, metrics, widths, "RightOne", "Selected", 1);
        PlaceSlot(b, metrics, widths, "RightTwo", "RightOne", 1);
    }

    private static void PlaceSlot(SwfBindings b, ISwfMetrics metrics, Dictionary<string, double> widths, string slot, string next, int side)
    {
        if (!widths.TryGetValue(slot, out var width) || !widths.TryGetValue(next, out var nextWidth)
            || !metrics.Origin(Carousel + slot, out var ox, out _) || !metrics.Origin(Carousel + next, out var nx, out _))
            return;
        var anchor = nx + (b.Place.TryGetValue(Carousel + next, out var moved) ? moved.Tx : 0);
        b.Place[Carousel + slot] = new SwfPlace(1, anchor + side * (nextWidth / 2 + width / 2 + TabSpacing) - ox, 0);
    }
}
