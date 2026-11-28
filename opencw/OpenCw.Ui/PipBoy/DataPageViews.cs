using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed class WorkshopListView : PipBoyListView<Workshop>
{
    private readonly QuestLogModel _log;
    private readonly WorkshopLog _workshops;

    public WorkshopListView(QuestLogModel log)
        : base((log.Workshops ?? throw new ArgumentException("The log has no workshop page.", nameof(log))).View)
    {
        _log = log;
        _workshops = log.Workshops!;
    }

    public event Action? PageChanged;

    public event Action<ShowOnMapResult>? MapRequested;

    public Func<string, bool>? MapJump { get; set; }

    public override IReadOnlyList<string> Tabs => DataTabs.Of(_log);

    public override int TabIndex => DataTabs.IndexOf(_log);

    public override string Summary => _workshops.EmptyMessage.Length > 0
        ? _workshops.EmptyMessage
        : string.Create(CultureInfo.InvariantCulture, $"{RowCount} workshops");

    protected override IEnumerable<MenuHint> ExtraHints =>
        _log.CurrentMapTarget is null ? Array.Empty<MenuHint>() : new[] { new MenuHint(MenuInputAction.Compare, "Show on map") };

    public override bool Handle(MenuInputAction action) => action == MenuInputAction.Compare ? ShowOnMap() : base.Handle(action);

    public bool ShowOnMap()
    {
        if (_log.CurrentMapTarget is not { } target || MapJump is { } jump && !jump(target)) return false;
        var result = _log.ShowOnMap();
        if (result is null) return false;
        MapRequested?.Invoke(result);
        return true;
    }

    public override IReadOnlyList<MenuHint> Hints =>
        _log.CurrentMapTarget is null ? base.Hints.Where(hint => hint.Action != MenuInputAction.Accept).ToArray() : base.Hints;

    protected override string AcceptLabel => "Show on map";

    protected override bool OnAccept(Workshop item) => ShowOnMap();

    protected override string TextOf(Workshop item) => WorkshopLog.RowText(item);

    protected override void ApplyTab(int index)
    {
        DataTabs.Apply(_log, index);
        PageChanged?.Invoke();
    }
}

public sealed class StatisticsListView : PipBoyListView<StatCategory>
{
    private readonly QuestLogModel _log;
    private readonly StatisticsLog _statistics;

    public StatisticsListView(QuestLogModel log)
        : base((log.Statistics ?? throw new ArgumentException("The log has no statistics page.", nameof(log))).View)
    {
        _log = log;
        _statistics = log.Statistics!;
    }

    public event Action? PageChanged;

    public override IReadOnlyList<string> Tabs => DataTabs.Of(_log);

    public override int TabIndex => DataTabs.IndexOf(_log);

    public override string Summary => _statistics.EmptyMessage.Length > 0
        ? _statistics.EmptyMessage
        : string.Create(CultureInfo.InvariantCulture, $"{RowCount} categories");

    protected override string TextOf(StatCategory item) => item.Name;

    protected override string DetailOf(StatCategory item) => item.Entries.Count.ToString(CultureInfo.InvariantCulture);

    protected override void ApplyTab(int index)
    {
        DataTabs.Apply(_log, index);
        PageChanged?.Invoke();
    }
}
