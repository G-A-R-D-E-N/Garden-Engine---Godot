using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Swf;

public sealed record PipboyPlan(string Movie, bool ArtList, bool ArtDetail, IReadOnlyList<string> Required, string? ListAnchor = null, double ContentTop = PipboyPages.ContentTop);

public static class PipboyPages
{
    public const string DataMovie = "Interface/Pipboy_DataPage.swf";
    public const string RadioMovie = "Interface/Pipboy_RadioPage.swf";
    public const double ContentLeft = 30;
    public const double ContentTop = 112;
    public const double ContentWidth = 816;
    public const double ContentBottom = 582;
    public const double RadioContentTop = 410;

    private static readonly PipboyPlan Status = new(PipboyArt.StatsMovie, false, true,
        ["StatusTab_mc", "SPECIALTab_mc", "StatusTab_mc/ConditionBoy_mc"]);

    private static readonly PipboyPlan Special = new(PipboyArt.StatsMovie, true, true,
        ["SPECIALTab_mc/List_mc", "SPECIALTab_mc/Description_tf", "StatusTab_mc"], "SPECIALTab_mc/List_mc");

    private static readonly PipboyPlan Inventory = new(PipboyInvArt.Movie, true, false, ["List_mc"], "List_mc");

    private static readonly PipboyPlan Quests = new(DataMovie, true, true,
        ["QuestsTab_mc/QuestsList_mc", "QuestsTab_mc/Description_tf", "QuestsTab_mc/ObjectivesList_mc", "StatsTab_mc", "WorkshopsTab_mc"], "QuestsTab_mc/QuestsList_mc");

    private static readonly PipboyPlan Statistics = new(DataMovie, true, true,
        ["StatsTab_mc/CategoryList_mc", "StatsTab_mc/ValueList_mc", "QuestsTab_mc", "WorkshopsTab_mc"], "StatsTab_mc/CategoryList_mc");

    private static readonly PipboyPlan Workshops = new(DataMovie, true, false,
        ["WorkshopsTab_mc/List_mc", "QuestsTab_mc", "StatsTab_mc"], "WorkshopsTab_mc/List_mc");

    private static readonly PipboyPlan Radio = new(RadioMovie, true, false, ["List_mc", "RadioWave_mc", "DeadWave_mc"], "List_mc", RadioContentTop);

    public static PipboyPlan? Plan(PipBoyTab tab, StatusSubTab sub, DataPage data) => tab switch
    {
        PipBoyTab.Stats => sub == StatusSubTab.Status ? Status : Special,
        PipBoyTab.Inventory => Inventory,
        PipBoyTab.Data => data switch
        {
            DataPage.Quests => Quests,
            DataPage.Statistics => Statistics,
            DataPage.Workshops => Workshops,
            _ => null,
        },
        PipBoyTab.Radio => Radio,
        _ => null,
    };

    public static (double X, double Y, double Width, double Height) Content(PipboyPlan? plan)
    {
        var top = plan?.ContentTop ?? ContentTop;
        return (ContentLeft, top, ContentWidth, ContentBottom - top);
    }

    public static int HitRow(int first, int count, double top, double height, double y, int rows)
    {
        if (rows <= 0 || height <= 0 || y < top || y >= top + height)
            return -1;
        var row = first + (int)((y - top) / (height / rows));
        return row < count ? row : -1;
    }

    public static bool CanScroll(int count, int visible) => count > visible;

    public static int ClampSelection(int selected, int first, int count, int visible)
    {
        var last = Math.Min(first + visible, count) - 1;
        return selected < 0 || last < first ? selected : Math.Clamp(selected, first, last);
    }

    public static (double X, double Y, double Width, double Height) StarRect(double listLeft, double top, double height, int rows, int slot)
    {
        var row = height / rows;
        return (listLeft - row, top + slot * row, row, row);
    }

    public static string SortLabel(IReadOnlyList<string> names, int index, bool descending) =>
        index < 0 || index >= names.Count ? "Sort" : "Sort: " + names[index] + (descending ? " v" : " ^");

    public static bool Usable(PipboyPlan? plan, Func<string, bool> has) => plan is not null && plan.Required.All(has);
}
