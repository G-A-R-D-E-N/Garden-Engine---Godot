using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record WorkshopMetric(string Name, double? Value, int Rating = 0, bool Tracked = true);

public sealed record Workshop(string Id, string Name, IReadOnlyList<WorkshopMetric>? Metrics = null, int Rating = 0,
                              bool Raiders = false, string? MapTarget = null);

public sealed record StatCategory(string Name, IReadOnlyList<StatEntry> Entries);

public sealed class WorkshopLog
{
    public const string AttentionWord = "[NEEDS ATTENTION]";
    public const string RaiderWord = "[RAIDERS]";
    public const string Empty = "NO WORKSHOPS";
    public const string Unknown = "--";
    public const string NotTracked = "n/a";

    public static readonly IReadOnlyList<string> MetricNames = ["People", "Food", "Water", "Power", "Defense", "Beds", "Happiness"];

    private readonly List<Workshop> _items;

    public WorkshopLog(IEnumerable<Workshop>? items = null)
    {
        _items = (items ?? Array.Empty<Workshop>()).ToList();
        View = new MenuList<Workshop>(_items, w => w.Id, w => w.Name);
        View.SetSort(new SortKey<Workshop>("Name", (a, b) =>
        {
            var byName = string.CompareOrdinal(a.Name, b.Name);
            return byName != 0 ? byName : string.CompareOrdinal(a.Id, b.Id);
        }));
    }

    public MenuList<Workshop> View { get; }

    public IReadOnlyList<Workshop> Items => _items;

    public string EmptyMessage => _items.Count == 0 ? Empty : string.Empty;

    public static string RowText(Workshop workshop) =>
        workshop.Rating < 0 ? workshop.Name + " " + AttentionWord
        : workshop.Raiders ? workshop.Name + " " + RaiderWord
        : workshop.Name;

    public static IReadOnlyList<string> MetricLines(Workshop workshop) =>
        MetricNames.Select(name => MetricLine(name, workshop.Metrics?.FirstOrDefault(
            metric => string.Equals(metric.Name, name, StringComparison.OrdinalIgnoreCase)))).ToArray();

    public void Replace(IEnumerable<Workshop> items)
    {
        _items.Clear();
        _items.AddRange(items);
        View.Refresh();
    }

    public void Upsert(Workshop workshop)
    {
        var index = _items.FindIndex(w => w.Id == workshop.Id);
        if (index >= 0) _items[index] = workshop;
        else _items.Add(workshop);
        View.Refresh();
    }

    public bool Select(string id) => View.SelectVisible(View.Visible.ToList().FindIndex(w => w.Id == id));

    private static string MetricLine(string name, WorkshopMetric? metric)
    {
        var value = metric?.Value is { } number && double.IsFinite(number)
            ? number.ToString("0.##", CultureInfo.InvariantCulture)
            : metric is { Tracked: false } ? NotTracked : Unknown;
        var word = metric?.Rating switch { < 0 => "ALERT", > 0 => "RISING", _ => string.Empty };
        return word.Length == 0 ? name + "  " + value : name + "  " + value + "  " + word;
    }
}

public sealed class StatisticsLog
{
    public const string Empty = "NO STATISTICS";
    public const string DefaultCategory = "General";

    private readonly List<StatCategory> _categories = new();

    public StatisticsLog(IEnumerable<StatEntry>? entries = null)
    {
        Group(entries ?? Array.Empty<StatEntry>());
        View = new MenuList<StatCategory>(_categories, c => c.Name, c => c.Name);
    }

    public MenuList<StatCategory> View { get; }

    public IReadOnlyList<StatCategory> Categories => _categories;

    public string EmptyMessage => _categories.Count == 0 ? Empty : string.Empty;

    public void Replace(IEnumerable<StatEntry> entries)
    {
        Group(entries);
        View.Refresh();
    }

    public bool Select(string name) => View.SelectVisible(View.Visible.ToList().FindIndex(c => c.Name == name));

    private void Group(IEnumerable<StatEntry> entries)
    {
        _categories.Clear();
        foreach (var group in entries.GroupBy(e => e.Category.Length == 0 ? DefaultCategory : e.Category))
            _categories.Add(new StatCategory(group.Key, group.ToArray()));
    }
}
