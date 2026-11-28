using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class PipboyDataArt
{
    private const string QuestsTab = "QuestsTab_mc";
    private const string StatisticsTab = "StatsTab_mc";
    private const string WorkshopsTab = "WorkshopsTab_mc";
    private const string SpecialList = "SPECIALTab_mc/List_mc";

    public static SwfBindings Stats(StatusListView view, SwfScroll? scroll = null)
    {
        var b = PipboyArt.StatsPage(view);
        if (view.Status.SubTab == StatusSubTab.Status)
            return b;
        var window = SwfScroll.Window(scroll, SpecialList, Rows(view));
        b.Lists[SpecialList] = window.Rows;
        b.Show.Add(SpecialList);
        return b;
    }

    public static SwfBindings Quests(QuestListView view, SwfScroll? scroll = null)
    {
        var b = new SwfBindings();
        b.Hide.Add(StatisticsTab);
        b.Hide.Add(WorkshopsTab);
        b.Hide.Add(QuestsTab + "/VaultBoyHolder_mc");
        List(b, QuestsTab + "/QuestsList_mc", view, scroll);
        var quest = view.Selected;
        b.Text[QuestsTab + "/Description_tf"] = quest?.Description ?? "";
        var objectives = (quest?.Objectives ?? []).Select(o => new SwfListRow((o.Completed ? "[x] " : "[ ] ") + o.Text + (o.Optional ? " (optional)" : ""), false)).ToList();
        if (objectives.Count == 0)
        {
            b.Hide.Add(QuestsTab + "/ObjectivesList_mc");
            return b;
        }
        var window = SwfBindings.Window(objectives);
        b.Lists[QuestsTab + "/ObjectivesList_mc"] = window.Rows;
        Frame(b, QuestsTab + "/ObjectivesList_mc", window);
        return b;
    }

    public static SwfBindings Statistics(StatisticsListView view, SwfScroll? scroll = null)
    {
        var b = new SwfBindings();
        b.Hide.Add(QuestsTab);
        b.Hide.Add(WorkshopsTab);
        b.Show.Add(StatisticsTab);
        List(b, StatisticsTab + "/CategoryList_mc", view, scroll);
        var entries = (view.Selected?.Entries ?? []).Select(e => new SwfListRow(e.Label + "  " + e.Value, false)).ToList();
        var window = SwfBindings.Window(entries);
        b.Lists[StatisticsTab + "/ValueList_mc"] = window.Rows;
        Frame(b, StatisticsTab + "/ValueList_mc", window);
        return b;
    }

    public static SwfBindings Workshops(WorkshopListView view, SwfScroll? scroll = null)
    {
        var b = new SwfBindings();
        b.Hide.Add(QuestsTab);
        b.Hide.Add(StatisticsTab);
        b.Show.Add(WorkshopsTab);
        b.Hide.Add(WorkshopsTab + "/ItemCard_mc");
        List(b, WorkshopsTab + "/List_mc", view, scroll);
        return b;
    }

    public static SwfBindings Radio(RadioListView view, SwfScroll? scroll = null)
    {
        var b = new SwfBindings();
        List(b, "List_mc", view, scroll);
        if (view.Radio.PlayingId is not null)
        {
            b.Show.Add("RadioWave_mc");
            b.Hide.Add("DeadWave_mc");
        }
        else
        {
            b.Hide.Add("RadioWave_mc");
        }
        return b;
    }

    public static IReadOnlyList<SwfListRow> Rows(IListMenuView view)
    {
        var rows = new List<SwfListRow>();
        for (var i = 0; i < view.RowCount; i++)
        {
            var detail = view.RowDetail(i);
            rows.Add(new SwfListRow(detail.Length > 0 ? view.RowText(i) + "  " + detail : view.RowText(i), i == view.SelectedIndex));
        }
        return rows;
    }

    private static void List(SwfBindings b, string path, IListMenuView view, SwfScroll? scroll)
    {
        var window = SwfScroll.Window(scroll, path, Rows(view));
        b.Lists[path] = window.Rows;
        Frame(b, path, window);
    }

    private static void Frame(SwfBindings b, string path, SwfListWindow window)
    {
        b.Show.Add(path);
        b.Hide.Add(path + "/border");
        (window.MoreAbove ? b.Show : b.Hide).Add(path + "/ScrollUp");
        (window.MoreBelow ? b.Show : b.Hide).Add(path + "/ScrollDown");
    }
}
