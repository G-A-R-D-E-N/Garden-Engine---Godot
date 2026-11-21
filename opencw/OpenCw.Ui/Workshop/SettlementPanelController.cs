using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Workshop;

public enum SettlementTab
{
    Overview,
    Settlers,
    Routes,
}

public enum PanelLevel
{
    Settlements,
    Pane,
    Actions,
}

public sealed record PanelRow(string Key, string Text, bool Disabled = false, string Reason = "");

public sealed class SettlementPanelController : ListDetailController<PanelRow>
{
    public const string NoSettlementsText = "No settlements.";
    public const string NoSettlersText = "No settlers here.";
    public const string NoRoutesText = "No routes.";
    public const string DoneText = "Done.";
    public const string NoValue = "--";

    public const string MoveKey = "move";
    public const string CaravanKey = "caravan";
    public const string JobKey = "job";
    public const string UnassignKey = "unassign";
    public const string RemoveKey = "remove";

    private enum Confirming
    {
        None,
        Unassign,
        RemoveRoute,
    }

    private readonly ISettlementProvider _provider;
    private readonly PanelLevel _floor;
    private PanelLevel _level;
    private SettlementTab _tab;
    private string _settlementId = string.Empty;
    private string _subjectId = string.Empty;
    private bool _subjectIsRoute;
    private Confirming _confirming;

    public SettlementPanelController(ISettlementProvider provider, bool embedded = false, string? settlementId = null)
        : base(new PlainListModel<PanelRow>(row => row.Key, row => row.Text))
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        Embedded = embedded;
        _floor = PanelLevel.Settlements;
        if (settlementId is not null && provider.Settlements.Any(settlement => settlement.Id == settlementId))
        {
            _settlementId = settlementId;
            _floor = PanelLevel.Pane;
        }
        _level = _floor;
        Rebuild(null);
        provider.Changed += OnProviderChanged;
        Finished += _ => provider.Changed -= OnProviderChanged;
    }

    public event Action<string, SettlerChange>? SettlerChanged;

    public event Action<DestinationPickerController>? PickerRequested;

    public event Action? ExitRequested;

    public bool Embedded { get; }

    public PanelLevel Level => _level;

    public SettlementTab Tab => _tab;

    public bool IsConfirming => _confirming != Confirming.None;

    public override string Title => "Settlements";

    public override string Header
    {
        get
        {
            if (_level == PanelLevel.Settlements) return string.Empty;
            var name = SettlementName(_settlementId);
            return _level == PanelLevel.Pane ? name + " - " + _tab : name + " - " + SubjectName();
        }
    }

    public override IReadOnlyList<MenuHint> Hints
    {
        get
        {
            if (IsConfirming)
                return new[] { new MenuHint(MenuInputAction.Accept, "Confirm"), new MenuHint(MenuInputAction.Back, "Cancel") };
            switch (_level)
            {
                case PanelLevel.Settlements:
                    return new[] { new MenuHint(MenuInputAction.Accept, "Open"), new MenuHint(MenuInputAction.Back, "Close") };
                case PanelLevel.Pane:
                    return new[]
                    {
                        new MenuHint(MenuInputAction.Accept, _tab == SettlementTab.Overview ? "Select" : "Actions"),
                        new MenuHint(MenuInputAction.TabPrev, "Previous tab"),
                        new MenuHint(MenuInputAction.TabNext, "Next tab"),
                        new MenuHint(MenuInputAction.Back, _level == _floor ? "Close" : "Back"),
                    };
                default:
                    return new[] { new MenuHint(MenuInputAction.Accept, "Do it"), new MenuHint(MenuInputAction.Back, "Back") };
            }
        }
    }

    public override IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            if (IsConfirming) return new[] { new MenuTextLine(ConfirmText(), LineTone.Heading) };
            if (Rows.RowCount == 0) return new[] { new MenuTextLine(EmptyText(), LineTone.Muted) };
            return _level switch
            {
                PanelLevel.Settlements => SettlementLines(Rows.Selected?.Key ?? string.Empty),
                PanelLevel.Pane => PaneLines(),
                _ => ActionLines(),
            };
        }
    }

    public static string JobWord(SettlerJob job) => job switch
    {
        SettlerJob.Food => "Farming",
        SettlerJob.Defense => "Guarding",
        SettlerJob.Provisioner => "Provisioner",
        SettlerJob.Scavenger => "Scavenging",
        SettlerJob.Vendor => "Vending",
        SettlerJob.Other => "Working",
        _ => "Unassigned",
    };

    public static string MetricText(SettlementMetric metric) =>
        metric.Label + ": " + (metric.Value is { } value ? value.ToString("0.##", CultureInfo.InvariantCulture) : NoValue);

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        if (IsConfirming) return HandleConfirm(action);
        switch (action)
        {
            case MenuInputAction.Accept:
                OnAccept();
                return true;
            case MenuInputAction.Back:
                OnBack();
                return true;
            case MenuInputAction.Left:
            case MenuInputAction.TabPrev:
                return ShiftTab(-1);
            case MenuInputAction.Right:
            case MenuInputAction.TabNext:
                return ShiftTab(1);
            default:
                return Rows.Handle(action);
        }
    }

    public void Refresh() => Rebuild(Rows.Selected?.Key);

    private SettlerInfo? Settler(string id) => _provider.Settlers.FirstOrDefault(settler => settler.Id == id);

    private RouteInfo? Route(string id) => _provider.Routes.FirstOrDefault(route => route.Id == id);

    private string SettlementName(string id) =>
        _provider.Settlements.FirstOrDefault(settlement => settlement.Id == id)?.Name ?? id;

    private string SubjectName()
    {
        if (!_subjectIsRoute) return Settler(_subjectId)?.Name ?? _subjectId;
        var route = Route(_subjectId);
        return route is null ? _subjectId : SettlementName(route.EndA) + " to " + SettlementName(route.EndB);
    }

    private string EmptyText() => _level switch
    {
        PanelLevel.Settlements => NoSettlementsText,
        _ => _tab == SettlementTab.Routes ? NoRoutesText : NoSettlersText,
    };

    private string ConfirmText() =>
        _confirming == Confirming.Unassign
            ? "Take " + SubjectName() + " off their current task?"
            : "Remove the route " + SubjectName() + "?";

    private bool ShiftTab(int step)
    {
        if (_level != PanelLevel.Pane) return false;
        var count = Enum.GetValues<SettlementTab>().Length;
        _tab = (SettlementTab)(((int)_tab + step + count) % count);
        Sound("menu.tab");
        Rebuild(null);
        return true;
    }

    private void OnAccept()
    {
        switch (_level)
        {
            case PanelLevel.Settlements:
                if (Rows.Selected is not { } settlement) return;
                _settlementId = settlement.Key;
                _level = PanelLevel.Pane;
                _tab = SettlementTab.Overview;
                Sound("menu.forward");
                Rebuild(null);
                return;
            case PanelLevel.Pane:
                if (_tab == SettlementTab.Overview || Rows.Selected is not { } subject) return;
                _subjectId = subject.Key;
                _subjectIsRoute = _tab == SettlementTab.Routes;
                _level = PanelLevel.Actions;
                Sound("menu.forward");
                Rebuild(null);
                return;
            default:
                RunAction();
                return;
        }
    }

    private void OnBack()
    {
        switch (_level)
        {
            case PanelLevel.Actions:
                var subject = _subjectId;
                _level = PanelLevel.Pane;
                Sound("menu.back");
                Rebuild(subject);
                return;
            case PanelLevel.Pane when _floor == PanelLevel.Settlements:
                var settlement = _settlementId;
                _level = PanelLevel.Settlements;
                Sound("menu.back");
                Rebuild(settlement);
                return;
            default:
                if (Embedded) ExitRequested?.Invoke();
                else Finish(false);
                return;
        }
    }

    private bool HandleConfirm(MenuInputAction action)
    {
        if (action == MenuInputAction.Back)
        {
            _confirming = Confirming.None;
            Raise();
        }
        else if (action == MenuInputAction.Accept)
        {
            var kind = _confirming;
            _confirming = Confirming.None;
            if (kind == Confirming.Unassign) Report(_provider.Unassign(_subjectId), _subjectId, SettlerChange.Unassigned);
            else Report(_provider.RemoveRoute(_subjectId), _subjectId, SettlerChange.Route);
        }
        return true;
    }

    private void RunAction()
    {
        if (Rows.Selected is not { } row) return;
        if (row.Disabled)
        {
            Say(row.Reason.Length > 0 ? row.Reason : DestinationPickerController.NotAvailableText);
            Sound("menu.blocked");
            return;
        }
        switch (row.Key)
        {
            case MoveKey:
                OpenPicker(false);
                return;
            case CaravanKey:
                OpenPicker(true);
                return;
            case JobKey:
                OpenJobPicker();
                return;
            case UnassignKey:
                _confirming = Confirming.Unassign;
                Raise();
                return;
            case RemoveKey:
                _confirming = Confirming.RemoveRoute;
                Raise();
                return;
        }
    }

    private void OpenPicker(bool caravan)
    {
        var settlerId = _subjectId;
        if (Settler(settlerId) is not { } settler) return;
        var entries = _provider.Settlements
            .Where(settlement => settlement.Id != settler.HomeId)
            .Select(settlement => new DestinationEntry(settlement.Id, settlement.Name))
            .ToList();
        Request(DestinationPickerController.TryCreate(
            caravan ? "Assign route" : "Move settler",
            caravan ? "Set a route to {name}?" : "Send " + settler.Name + " to {name}?",
            entries,
            null,
            () => Alive(settlerId),
            id => Reported(caravan ? _provider.AssignCaravan(settlerId, id) : _provider.MoveHome(settlerId, id), settlerId,
                           caravan ? SettlerChange.Route : SettlerChange.Home)));
    }

    private void OpenJobPicker()
    {
        var settlerId = _subjectId;
        var entries = _provider.ListJobTargets(settlerId)
            .Select(target => target.TakenBy.Length > 0
                ? new DestinationEntry(target.ObjectId, target.Name, true, "Already staffed by " + target.TakenBy + ".")
                : new DestinationEntry(target.ObjectId, target.Name))
            .ToList();
        Request(DestinationPickerController.TryCreate(
            "Assign job",
            "Put " + SubjectName() + " to work at {name}?",
            entries,
            null,
            () => Alive(settlerId),
            id => Reported(_provider.AssignJob(settlerId, id), settlerId, SettlerChange.Job)));
    }

    private void Request(DestinationPickerController? picker)
    {
        if (picker is null)
        {
            Say(DestinationPickerController.NotAvailableText);
            Sound("menu.blocked");
            return;
        }
        PickerRequested?.Invoke(picker);
    }

    private bool Alive(string settlerId) => !IsFinished && _level == PanelLevel.Actions && _subjectId == settlerId;

    private ActionResult Reported(ActionResult result, string settlerId, SettlerChange change)
    {
        if (result.Ok) Report(result, settlerId, change);
        return result;
    }

    private void Report(ActionResult result, string subjectId, SettlerChange change)
    {
        if (!result.Ok)
        {
            Say(result.Reason.Length > 0 ? result.Reason : DestinationPickerController.FailedText);
            Sound("menu.blocked");
            return;
        }
        Say(result.Reason.Length > 0 ? DoneText + " " + result.Reason : DoneText);
        SettlerChanged?.Invoke(subjectId, change);
        if (!IsFinished) Reconcile();
    }

    private void OnProviderChanged()
    {
        if (!IsFinished) Reconcile();
    }

    private void Reconcile()
    {
        if (_level != PanelLevel.Settlements && _provider.Settlements.All(settlement => settlement.Id != _settlementId))
        {
            if (_floor == PanelLevel.Pane)
            {
                Finish(false);
                return;
            }
            _level = PanelLevel.Settlements;
        }
        if (_level == PanelLevel.Actions && !SubjectExists())
        {
            _confirming = Confirming.None;
            _level = PanelLevel.Pane;
        }
        Rebuild(Rows.Selected?.Key);
    }

    private bool SubjectExists() => _subjectIsRoute ? Route(_subjectId) is not null : Settler(_subjectId) is not null;

    private void Rebuild(string? selectKey)
    {
        IEnumerable<PanelRow> rows;
        switch (_level)
        {
            case PanelLevel.Settlements:
                rows = _provider.Settlements.Select(settlement =>
                    new PanelRow(settlement.Id, settlement.Raiders ? settlement.Name + " - Under attack" : settlement.Name));
                break;
            case PanelLevel.Pane:
                rows = PaneRows();
                break;
            default:
                rows = ActionRows();
                break;
        }
        Rows.ShowRows(rows.ToList(), selectKey);
        Rows.Summary = Header;
    }

    private IEnumerable<PanelRow> PaneRows()
    {
        switch (_tab)
        {
            case SettlementTab.Overview:
                var info = _provider.Settlements.FirstOrDefault(settlement => settlement.Id == _settlementId);
                return (info?.Metrics ?? Array.Empty<SettlementMetric>())
                    .Select(metric => new PanelRow(metric.Label, MetricText(metric)));
            case SettlementTab.Settlers:
                return _provider.Settlers.Where(settler => settler.HomeId == _settlementId)
                    .Select(settler => new PanelRow(settler.Id, settler.Name + " - " + JobWord(settler.Job)));
            default:
                return _provider.Routes.Where(route => route.EndA == _settlementId || route.EndB == _settlementId)
                    .Select(route => new PanelRow(route.Id, SettlementName(route.EndA) + " to " + SettlementName(route.EndB)));
        }
    }

    private IEnumerable<PanelRow> ActionRows()
    {
        if (_subjectIsRoute) return new[] { new PanelRow(RemoveKey, "Remove route") };
        if (Settler(_subjectId) is not { } settler) return Array.Empty<PanelRow>();
        var assigned = settler.Job != SettlerJob.Unassigned || settler.RouteId.Length > 0;
        return new[]
        {
            new PanelRow(MoveKey, "Move to another settlement", !settler.CanMove, settler.MoveReason),
            new PanelRow(CaravanKey, "Assign caravan route", !settler.CanCaravan, settler.CaravanReason),
            new PanelRow(JobKey, "Assign job", !settler.CanCommand, settler.CommandReason),
            new PanelRow(UnassignKey, "Unassign", !assigned, "Nothing to unassign."),
        };
    }

    private IReadOnlyList<MenuTextLine> SettlementLines(string id)
    {
        var info = _provider.Settlements.FirstOrDefault(settlement => settlement.Id == id);
        if (info is null) return Array.Empty<MenuTextLine>();
        var lines = new List<MenuTextLine> { new(info.Name, LineTone.Heading) };
        if (info.Raiders) lines.Add(new MenuTextLine("Under attack", LineTone.Bad));
        lines.AddRange(info.Metrics.Select(metric => new MenuTextLine(MetricText(metric), ToneOf(metric.Rating))));
        return lines;
    }

    private IReadOnlyList<MenuTextLine> PaneLines()
    {
        var info = _provider.Settlements.FirstOrDefault(settlement => settlement.Id == _settlementId);
        if (_tab == SettlementTab.Overview)
        {
            var lines = new List<MenuTextLine>();
            if (info is { Raiders: true }) lines.Add(new MenuTextLine("Under attack", LineTone.Bad));
            if (Rows.Selected is { } row && info?.Metrics.FirstOrDefault(metric => metric.Label == row.Key) is { } selected)
                lines.Add(new MenuTextLine(MetricText(selected), ToneOf(selected.Rating)));
            return lines;
        }
        return SubjectLines(Rows.Selected?.Key ?? string.Empty, _tab == SettlementTab.Routes);
    }

    private IReadOnlyList<MenuTextLine> ActionLines()
    {
        var lines = new List<MenuTextLine>(SubjectLines(_subjectId, _subjectIsRoute));
        if (Rows.Selected is { Disabled: true } row)
            lines.Add(new MenuTextLine(row.Reason.Length > 0 ? row.Reason : DestinationPickerController.NotAvailableText, LineTone.Bad));
        return lines;
    }

    private IReadOnlyList<MenuTextLine> SubjectLines(string id, bool route)
    {
        var lines = new List<MenuTextLine>();
        if (route)
        {
            if (Route(id) is not { } info) return lines;
            lines.Add(new MenuTextLine(SettlementName(info.EndA) + " to " + SettlementName(info.EndB), LineTone.Heading));
            if (Settler(info.SettlerId) is { } runner) lines.Add(new MenuTextLine("Runs: " + runner.Name));
            return lines;
        }
        if (Settler(id) is not { } settler) return lines;
        lines.Add(new MenuTextLine(settler.Name, LineTone.Heading));
        lines.Add(new MenuTextLine("Home: " + SettlementName(settler.HomeId)));
        lines.Add(new MenuTextLine("Job: " + JobWord(settler.Job)));
        if (settler.WorkObject.Length > 0) lines.Add(new MenuTextLine("Works at: " + settler.WorkObject));
        if (settler.RouteId.Length > 0 && Route(settler.RouteId) is { } assigned)
            lines.Add(new MenuTextLine("Route: " + SettlementName(assigned.EndA) + " to " + SettlementName(assigned.EndB)));
        return lines;
    }

    private static LineTone ToneOf(int rating) => rating < 0 ? LineTone.Bad : rating > 0 ? LineTone.Good : LineTone.Normal;
}
