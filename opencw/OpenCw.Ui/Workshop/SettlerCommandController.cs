using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Workshop;

public enum CommandLevel
{
    Settlers,
    Commands,
    Targets,
}

public sealed record CommandRow(string Key, string Text, bool Disabled = false, string Reason = "");

public sealed class SettlerCommandController : ListDetailController<CommandRow>, IDisposable
{
    public const string CommandKey = "command";
    public const string MoveKey = "move";
    public const string CaravanKey = "caravan";
    public const string NoSettlersText = "No settlers here.";
    public const string NoTargetsText = "Nothing to work on.";
    public const string NotAvailableText = "Not available.";
    public const string ChangedText = "That changed.";
    public const string FailedText = "That didn't work.";

    private readonly ISettlementProvider _provider;
    private readonly CommandLevel _floor;
    private CommandLevel _level;
    private string _settlerId = string.Empty;
    private IReadOnlyList<JobTarget> _targets = Array.Empty<JobTarget>();
    private bool _busy;

    public SettlerCommandController(ISettlementProvider provider, string? settlerId = null)
        : base(new PlainListModel<CommandRow>(row => row.Key, row => row.Text))
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _floor = CommandLevel.Settlers;
        if (settlerId is not null && provider.Settlers.Any(settler => settler.Id == settlerId))
        {
            _settlerId = settlerId;
            _floor = CommandLevel.Commands;
        }
        _level = _floor;
        Rebuild(null);
        Rows.Accepted += _ => OnAccept();
        provider.Changed += OnProviderChanged;
        Finished += _ => provider.Changed -= OnProviderChanged;
    }

    public event Action<string, SettlerChange>? SettlerChanged;

    public event Action<DestinationPickerController>? PickerRequested;

    public void Dispose() => _provider.Changed -= OnProviderChanged;

    public CommandLevel Level => _level;

    public string SettlerId => _settlerId;

    public override string Title => "Settler orders";

    public override string Header => _level switch
    {
        CommandLevel.Settlers => string.Empty,
        CommandLevel.Commands => SettlerName(),
        _ => "Choose a task for " + SettlerName() + ".",
    };

    public override IReadOnlyList<MenuHint> Hints => _level switch
    {
        CommandLevel.Settlers => new[] { new MenuHint(MenuInputAction.Accept, "Select"), new MenuHint(MenuInputAction.Back, "Close") },
        CommandLevel.Commands => new[]
        {
            new MenuHint(MenuInputAction.Accept, "Choose"),
            new MenuHint(MenuInputAction.Back, _floor == CommandLevel.Commands ? "Close" : "Back"),
        },
        _ => new[] { new MenuHint(MenuInputAction.Accept, "Assign"), new MenuHint(MenuInputAction.Back, "Cancel") },
    };

    public override IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            var lines = new List<MenuTextLine>();
            if (Rows.RowCount == 0)
            {
                lines.Add(new MenuTextLine(_level == CommandLevel.Settlers ? NoSettlersText : NoTargetsText, LineTone.Muted));
                return lines;
            }
            switch (_level)
            {
                case CommandLevel.Settlers:
                    if (Settler(Rows.Selected?.Key ?? string.Empty) is { } picked) AddCard(lines, picked);
                    break;
                case CommandLevel.Commands:
                    if (Settler(_settlerId) is { } subject) AddCard(lines, subject);
                    if (Rows.Selected is { Disabled: true } blocked)
                        lines.Add(new MenuTextLine(blocked.Reason.Length > 0 ? blocked.Reason : NotAvailableText, LineTone.Bad));
                    break;
                default:
                    if (_targets.FirstOrDefault(t => t.ObjectId == Rows.Selected?.Key) is { } found)
                        AddTarget(lines, found);
                    break;
            }
            return lines;
        }
    }

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        if (_busy) return true;
        switch (action)
        {
            case MenuInputAction.Accept:
                OnAccept();
                return true;
            case MenuInputAction.Back:
                OnBack();
                return true;
            default:
                return Rows.Handle(action);
        }
    }

    private SettlerInfo? Settler(string id) => _provider.Settlers.FirstOrDefault(settler => settler.Id == id);

    private string SettlerName() => Settler(_settlerId)?.Name ?? _settlerId;

    private string HomeName(string homeId) =>
        _provider.Settlements.FirstOrDefault(settlement => settlement.Id == homeId)?.Name ?? homeId;

    private static void AddCard(List<MenuTextLine> lines, SettlerInfo settler)
    {
        lines.Add(new MenuTextLine(settler.Name, LineTone.Heading));
        lines.Add(new MenuTextLine(SettlementPanelController.JobWord(settler.Job)
            + (settler.WorkObject.Length > 0 ? " - " + settler.WorkObject : string.Empty)));
    }

    private static void AddTarget(List<MenuTextLine> lines, JobTarget target)
    {
        lines.Add(new MenuTextLine(target.Name, LineTone.Heading));
        lines.Add(new MenuTextLine("Needs a worker: " + (target.NeedsWorker ? "yes" : "no")));
        if (target.TakenBy.Length > 0) lines.Add(new MenuTextLine("In use by " + target.TakenBy, LineTone.Bad));
    }

    private void Rebuild(string? selectKey)
    {
        IEnumerable<CommandRow> rows;
        switch (_level)
        {
            case CommandLevel.Settlers:
                rows = _provider.Settlers.Select(settler => new CommandRow(settler.Id, settler.Name + " - " + HomeName(settler.HomeId)));
                break;
            case CommandLevel.Commands:
                rows = CommandRows();
                break;
            default:
                rows = _targets.Select(target => new CommandRow(target.ObjectId,
                    target.TakenBy.Length > 0 ? target.Name + " - in use" : target.Name, target.TakenBy.Length > 0,
                    "In use by " + target.TakenBy + "."));
                break;
        }
        Rows.ShowRows(rows.ToList(), selectKey);
        Rows.Summary = Header;
    }

    private IEnumerable<CommandRow> CommandRows()
    {
        if (Settler(_settlerId) is not { } settler) return Array.Empty<CommandRow>();
        return new[]
        {
            new CommandRow(CommandKey, "Command", !settler.CanCommand, Or(settler.CommandReason, settler.Name + " won't take orders from you.")),
            new CommandRow(MoveKey, "Move", !settler.CanMove, Or(settler.MoveReason, settler.Name + " can't be moved from this settlement.")),
            new CommandRow(CaravanKey, "Caravan", !settler.CanCaravan, Or(settler.CaravanReason, settler.Name + " can't be sent on a caravan route.")),
        };
    }

    private static string Or(string reason, string fallback) => reason.Length > 0 ? reason : fallback;

    private void Blocked(string text)
    {
        Say(text);
        Sound("menu.blocked");
    }

    private void GoTo(CommandLevel level, string? selectKey)
    {
        var forward = level > _level;
        _level = level;
        Sound(forward ? "menu.forward" : "menu.back");
        Rebuild(selectKey);
    }

    private void OnBack()
    {
        switch (_level)
        {
            case CommandLevel.Targets:
                GoTo(CommandLevel.Commands, CommandKey);
                return;
            case CommandLevel.Commands when _floor == CommandLevel.Settlers:
                GoTo(CommandLevel.Settlers, _settlerId);
                return;
            default:
                Finish(false);
                return;
        }
    }

    private void OnAccept()
    {
        if (IsFinished || _busy || Rows.Selected is not { } row) return;
        switch (_level)
        {
            case CommandLevel.Settlers:
                _settlerId = row.Key;
                GoTo(CommandLevel.Commands, null);
                return;
            case CommandLevel.Commands:
                RunCommand(row);
                return;
            default:
                Assign(row);
                return;
        }
    }

    private void RunCommand(CommandRow row)
    {
        if (row.Disabled)
        {
            Blocked(row.Reason.Length > 0 ? row.Reason : NotAvailableText);
            return;
        }
        if (row.Key == CommandKey)
        {
            try
            {
                _targets = _provider.ListJobTargets(_settlerId);
            }
            catch (Exception)
            {
                Blocked(FailedText);
                return;
            }
            GoTo(CommandLevel.Targets, null);
            return;
        }
        OpenPicker(row.Key == CaravanKey);
    }

    private void Assign(CommandRow row)
    {
        if (row.Disabled)
        {
            Blocked(row.Reason.Length > 0 ? row.Reason : NotAvailableText);
            return;
        }
        var settlerId = _settlerId;
        var name = SettlerName();
        _busy = true;
        ActionResult result;
        try
        {
            result = _provider.AssignJob(settlerId, row.Key);
        }
        catch (Exception)
        {
            result = new ActionResult(false, FailedText);
        }
        finally
        {
            _busy = false;
        }
        if (IsFinished) return;
        if (!result.Ok)
        {
            Blocked(result.Reason.Length > 0 ? result.Reason : DestinationPickerController.FailedText);
            return;
        }
        Say(name + " is on the job." + (result.Reason.Length > 0 ? " " + result.Reason : ""));
        Sound("settler.assign");
        SettlerChanged?.Invoke(settlerId, SettlerChange.Job);
        Finish(true);
    }

    private void OpenPicker(bool caravan)
    {
        var settlerId = _settlerId;
        if (Settler(settlerId) is not { } settler) return;
        var entries = _provider.Settlements
            .Where(settlement => settlement.Id != settler.HomeId)
            .Select(settlement => new DestinationEntry(settlement.Id, settlement.Name))
            .ToList();
        var picker = DestinationPickerController.TryCreate(
            caravan ? "Assign route" : "Move settler",
            caravan ? "Set a route to {name}?" : "Send " + settler.Name + " to {name}?",
            entries,
            null,
            () => !IsFinished && _level == CommandLevel.Commands && _settlerId == settlerId,
            id =>
            {
                ActionResult result;
                try
                {
                    result = caravan ? _provider.AssignCaravan(settlerId, id) : _provider.MoveHome(settlerId, id);
                }
                catch (Exception)
                {
                    return new ActionResult(false, FailedText);
                }
                if (result.Ok) SettlerChanged?.Invoke(settlerId, caravan ? SettlerChange.Route : SettlerChange.Home);
                return result;
            });
        if (picker is null || PickerRequested is null)
        {
            Blocked(NotAvailableText);
            return;
        }
        PickerRequested(picker);
    }

    private void OnProviderChanged()
    {
        if (IsFinished) return;
        if (_level != CommandLevel.Settlers && Settler(_settlerId) is null)
        {
            Finish(false);
            return;
        }
        if (_level == CommandLevel.Targets) _targets = _provider.ListJobTargets(_settlerId);
        Rebuild(Rows.Selected?.Key);
    }
}
