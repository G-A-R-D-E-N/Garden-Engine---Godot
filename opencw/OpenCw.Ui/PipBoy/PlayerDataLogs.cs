using System;
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

    public const string PeopleMetric = "People";
    public const string BedsMetric = "Beds";

    // Invariant: only People and Beds are measured; the other metrics are listed as not tracked, so the page never shows a number it did not read. No raid state is tracked, so Raiders stays false.
    public static WorkshopLog? Workshops(PlayerSettlements settlements, Func<double, double, string?>? markerNear = null, string? world = null) =>
        settlements.IsEmpty ? null : new WorkshopLog(settlements.All.Select(known => Row(known, markerNear, world)));

    private static Workshop Row(KnownSettlement known, Func<double, double, string?>? markerNear, string? world)
    {
        var metrics = new List<WorkshopMetric>
        {
            new(PeopleMetric, known.People),
            new(BedsMetric, known.Beds, known.People is { } people && known.Beds is { } beds && beds < people ? -1 : 0),
        };
        metrics.AddRange(WorkshopLog.MetricNames.Where(name => name is not (PeopleMetric or BedsMetric)).Select(name => new WorkshopMetric(name, null, 0, false)));
        var target = known.MapX is { } x && known.MapY is { } y && (known.World is null || world is null || known.World == world) ? markerNear?.Invoke(x, y) : null;
        return new Workshop(known.Id, known.Name, metrics, metrics.Any(metric => metric.Rating < 0) ? -1 : 0, false, target);
    }
}
