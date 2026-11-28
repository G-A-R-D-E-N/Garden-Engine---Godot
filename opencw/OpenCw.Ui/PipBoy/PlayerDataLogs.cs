using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class PlayerDataLogs
{
    // Invariant: a counter at 0 is left out, and no counter at all means no Statistics page.
    public static StatisticsLog? Statistics(PlayerStats stats)
    {
        var entries = new List<StatEntry>();
        foreach (var (key, label) in PlayerStats.Counters)
        {
            var count = stats.Count(key);
            if (count > 0) entries.Add(new StatEntry(key, label, count.ToString(CultureInfo.InvariantCulture), StatisticsLog.DefaultCategory));
        }
        return entries.Count == 0 ? null : new StatisticsLog(entries);
    }

    public static WorkshopLog? Workshops(PlayerSettlements settlements) =>
        settlements.IsEmpty ? null : new WorkshopLog(settlements.All.Select(known => new Workshop(known.Id, known.Name)));
}
