using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.PipBoy;

internal static class DataTabs
{
    public const string NotesName = "Notes";
    public const string WorkshopsName = "Workshops";
    public const string StatisticsName = "Statistics";

    private static readonly string[] Filters = ["Active", "Completed", "All"];

    // Invariant: the three status filters come first in enum order, then Notes when offered, Workshops and Statistics when their logs exist.
    public static IReadOnlyList<string> Of(QuestLogModel log)
    {
        var names = new List<string>(Filters);
        if (log.NotesOffered) names.Add(NotesName);
        if (log.Workshops is not null) names.Add(WorkshopsName);
        if (log.Statistics is not null) names.Add(StatisticsName);
        return names;
    }

    public static int IndexOf(QuestLogModel log)
    {
        var name = log.Page switch { DataPage.Notes => NotesName, DataPage.Workshops => WorkshopsName, DataPage.Statistics => StatisticsName, _ => null };
        var at = name is null ? -1 : Of(log).ToList().IndexOf(name);
        return at < 0 ? (int)log.StatusFilter : at;
    }

    public static void Apply(QuestLogModel log, int index)
    {
        var names = Of(log);
        if (index < 0 || index >= names.Count) return;
        switch (names[index])
        {
            case NotesName: log.SetPage(DataPage.Notes); break;
            case WorkshopsName: log.SetPage(DataPage.Workshops); break;
            case StatisticsName: log.SetPage(DataPage.Statistics); break;
            default:
                log.SetStatusFilter((QuestStatusFilter)index);
                log.SetPage(DataPage.Quests);
                break;
        }
    }
}

public sealed class QuestListView : PipBoyListView<Quest>
{
    private static readonly string[] Sorts = ["Recent", "Name", "Group"];

    private readonly QuestLogModel _log;

    public QuestListView(QuestLogModel log)
        : base(log.Quests)
    {
        _log = log;
    }

    public event Action<QuestEvent>? Command;

    public event Action? PageChanged;

    public event Action<ShowOnMapResult>? MapRequested;

    public Func<string, bool>? MapJump { get; set; }

    public override IReadOnlyList<string> Tabs => DataTabs.Of(_log);

    public override int TabIndex => DataTabs.IndexOf(_log);

    public override IReadOnlyList<string> SortNames => Sorts;

    public override int SortIndex => (int)_log.Sort;

    public override bool SortDescending => _log.SortDescending;

    public override string Summary =>
        FormattableString.Invariant($"{RowCount} quests") + (_log.TrackedQuest is { } tracked ? ", tracking " + tracked.Name : string.Empty);

    protected override string AcceptLabel =>
        Selected is not { } quest ? "Select" : quest.Completed ? "View" : quest.Id == _log.TrackedId ? "Stop tracking" : "Track";

    protected override string? FavouriteLabel =>
        Selected is { Completed: false } open ? open.Id == _log.TrackedId ? "Stop tracking" : "Track" : null;

    protected override IEnumerable<MenuHint> ExtraHints =>
        _log.CurrentMapTarget is null ? Array.Empty<MenuHint>() : new[] { new MenuHint(MenuInputAction.Compare, "Show on map") };

    public override bool Handle(MenuInputAction action) => action == MenuInputAction.Compare ? ShowOnMap() : base.Handle(action);

    public bool ShowOnMap()
    {
        if (_log.CurrentMapTarget is not { } target || MapJump is { } jump && !jump(target)) return false;
        var result = _log.ShowOnMap();
        if (result is null) return false;
        if (result.Tracked is { } tracked) Command?.Invoke(tracked);
        MapRequested?.Invoke(result);
        Refresh();
        return true;
    }

    protected override string TextOf(Quest item) => item.Name;

    protected override string DetailOf(Quest item) => item.Group;

    protected override bool IsFavourite(Quest item) => item.Id == _log.TrackedId;

    protected override bool ToggleFavourite()
    {
        var result = _log.TrackCurrent();
        if (result is not null) Command?.Invoke(result);
        return result is not null;
    }

    protected override bool OnAccept(Quest item) => ToggleFavourite() || item.Completed;

    protected override void ApplyTab(int index)
    {
        DataTabs.Apply(_log, index);
        PageChanged?.Invoke();
    }

    protected override void ApplySort(int index, bool descending) => _log.SetSort((QuestSort)index, descending);
}

public sealed class NoteListView : PipBoyListView<Note>
{
    private readonly QuestLogModel _log;

    public NoteListView(QuestLogModel log)
        : base(log.Notes)
    {
        _log = log;
    }

    public event Action? PageChanged;

    public override IReadOnlyList<string> Tabs => DataTabs.Of(_log);

    public override int TabIndex => DataTabs.IndexOf(_log);

    public override string Summary => string.Create(CultureInfo.InvariantCulture, $"{RowCount} notes");

    protected override string TextOf(Note item) => item.Title;

    protected override void ApplyTab(int index)
    {
        DataTabs.Apply(_log, index);
        PageChanged?.Invoke();
    }
}
