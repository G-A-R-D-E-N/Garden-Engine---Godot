using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class PipboyArt
{
    public const string ShellMovie = "Interface/PipboyMenu.swf";
    public const string StatsMovie = "Interface/Pipboy_StatsPage.swf";

    private const string Info = "Menu_mc/BottomBar_mc/Info_mc/";
    private const string Header = "Menu_mc/Header_mc/";
    private const string StatusTab = "StatusTab_mc";
    private const string SpecialTab = "SPECIALTab_mc";

    private static readonly (string Meter, string Key)[] Meters =
    {
        ("Head_Meter", PlayerStatusModel.LimbKey(LimbKind.Head)),
        ("LArm_Meter", PlayerStatusModel.LimbKey(LimbKind.LeftArm)),
        ("RArm_Meter", PlayerStatusModel.LimbKey(LimbKind.RightArm)),
        ("LLeg_Meter", PlayerStatusModel.LimbKey(LimbKind.LeftLeg)),
        ("RLeg_Meter", PlayerStatusModel.LimbKey(LimbKind.RightLeg)),
        ("Torso_Meter", PlayerStatusModel.TorsoKey),
    };

    public static bool IsDrawnRow(StatusSubTab tab, string? key) =>
        tab != StatusSubTab.Status || key is not null
        && (key == PlayerStatusModel.TorsoKey || key == "effects" || Meters.Any(meter => meter.Key == key));

    public static bool Step(StatusListView view, MenuInputAction action)
    {
        var status = view.Status;
        var start = status.CurrentKey;
        var last = start;
        for (var i = 0; i < status.View.Visible.Count; i++)
        {
            view.Handle(action);
            var key = status.CurrentKey;
            if (IsDrawnRow(status.SubTab, key))
                return true;
            if (key == last)
                break;
            last = key;
        }
        if (start is not null)
            status.Select(start);
        return false;
    }

    public static void SelectFirstDrawn(StatusListView view)
    {
        var status = view.Status;
        if (IsDrawnRow(status.SubTab, status.CurrentKey))
            return;
        var index = status.View.Visible.ToList().FindIndex(row => IsDrawnRow(status.SubTab, row.Key));
        if (index >= 0)
            status.View.SelectVisible(index);
    }

    public static SwfBindings Shell(StatusListView view, IPipBoyStatusSource? source)
    {
        var b = new SwfBindings();
        var segments = PipBoyFooter.Segments(PipBoyTab.Stats, source);
        b.Text[Info + "HP_tf"] = Segment(segments, 0);
        b.Text[Info + "LVL_tf"] = Segment(segments, 1);
        b.Text[Info + "AP_tf"] = Segment(segments, 2);
        if (source?.Level is not null && source.LevelProgress is { } progress && double.IsFinite(progress))
            b.FillX[Info + "XPMeter_mc/Fill_mc"] = progress;
        else
            b.Hide.Add(Info + "XPMeter_mc");
        b.Text[Header + "TabHeader_mc/AlphaHolder/Selected/textField_tf"] = view.Tabs[view.TabIndex];
        foreach (var slot in new[] { "RightOne", "LeftOne", "RightTwo", "LeftTwo" })
            b.Hide.Add(Header + "TabHeader_mc/AlphaHolder/" + slot);
        b.Hide.Add(Header + "LeftTriggerButton_mc");
        b.Hide.Add(Header + "RightTriggerButton_mc");
        return b;
    }

    public static SwfBindings StatsPage(StatusListView view)
    {
        var b = new SwfBindings();
        if (view.Status.SubTab == StatusSubTab.Status)
            BindStatus(b, view.Status);
        else
            BindList(b, view);
        return b;
    }

    private static void BindStatus(SwfBindings b, PlayerStatusModel status)
    {
        b.Hide.Add(SpecialTab);
        b.Hide.Add(StatusTab + "/DMGWidget_mc");
        b.Hide.Add(StatusTab + "/DRWidget_mc");
        b.Text[StatusTab + "/Name_tf"] = status.Heading;
        var rows = status.Condition.ToDictionary(row => row.Key);
        foreach (var (meter, key) in Meters)
        {
            var path = StatusTab + "/" + meter;
            if (!rows.TryGetValue(key, out var row))
            {
                b.Hide.Add(path);
                continue;
            }
            b.FillX[path + "/Fill_mc"] = row.Fraction;
            b.FillX[path + "/GhostFill_mc"] = row.PendingFraction;
        }
        if (status.EffectsShown)
            b.Lists[StatusTab + "/ActiveEffects_mc"] = status.EffectLines
                .Skip(status.EffectScroll).Take(status.EffectViewRows).Select(line => new SwfListRow(line, false)).ToList();
    }

    private static void BindList(SwfBindings b, StatusListView view)
    {
        b.Hide.Add(StatusTab);
        var rows = new List<SwfListRow>();
        for (var i = view.ScrollTop; i < view.RowCount && i < view.ScrollTop + view.ViewRows; i++)
        {
            var detail = view.RowDetail(i);
            rows.Add(new SwfListRow(detail.Length > 0 ? view.RowText(i) + "  " + detail : view.RowText(i), i == view.SelectedIndex));
        }
        b.Lists[SpecialTab + "/List_mc"] = rows;
        b.Text[SpecialTab + "/Description_tf"] = Description(view.Status);
    }

    private static string Description(PlayerStatusModel status)
    {
        if (status.EmptyMessage.Length > 0)
            return status.EmptyMessage;
        return status.RankLine.Length > 0 ? status.RankLine + "\n" + status.SelectedDescription : status.SelectedDescription;
    }

    private static string Segment(IReadOnlyList<string> segments, int index) =>
        index < segments.Count ? segments[index] : PipBoyFooter.Unknown;
}
