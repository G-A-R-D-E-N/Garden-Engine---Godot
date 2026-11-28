namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class PipBoyTabPlan
{
    private static readonly PipBoyTab[] Samples =
        { PipBoyTab.Stats, PipBoyTab.Inventory, PipBoyTab.Data, PipBoyTab.Map, PipBoyTab.Radio };

    public static IReadOnlyList<PipBoyTab> Tabs(bool samples, bool hasInventory, bool hasQuestLog, bool hasRadio = false, bool hasMap = false, bool hasStats = false)
    {
        if (samples) return Samples;
        var tabs = new List<PipBoyTab>(5);
        if (hasStats) tabs.Add(PipBoyTab.Stats);
        if (hasInventory) tabs.Add(PipBoyTab.Inventory);
        if (hasQuestLog) tabs.Add(PipBoyTab.Data);
        if (hasMap) tabs.Add(PipBoyTab.Map);
        if (hasRadio) tabs.Add(PipBoyTab.Radio);
        return tabs;
    }

    public static IReadOnlyList<PipBoyTab> Header(IEnumerable<PipBoyTab> built) =>
        Enum.GetValues<PipBoyTab>().Where(built.ToHashSet().Contains).ToArray();

    public static PipBoyTab? Start(IReadOnlyList<PipBoyTab> tabs) => tabs.Count > 0 ? tabs[0] : null;
}
