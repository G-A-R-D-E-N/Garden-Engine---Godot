using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Dialogs;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Workbench;

public enum WorkbenchView
{
    Targets,
    Slots,
    Options,
    Confirm,
}

public sealed record BenchRow(string Key, string Text);

public sealed class WorkbenchController : ListDetailController<BenchRow>
{
    public const string EmptyText = "Nothing to work on.";
    public const string ChangedText = "That changed.";
    public const string DoneText = "Done.";
    public const string DefaultVerb = "Apply";
    public const string FailedText = "That didn't work.";
    public const string RenamedText = "Renamed.";
    public const string RenameTitle = "Rename";
    public const string RenamePrompt = "Enter a new name.";

    private readonly IWorkbenchRecipes _recipes;
    private WorkbenchView _view;
    private string _targetId = string.Empty;
    private string _slotId = string.Empty;
    private string _optionId = string.Empty;
    private string _stamp;
    private bool _crafting;
    private readonly IWorkbenchRename? _rename;

    public WorkbenchController(IWorkbenchRecipes recipes, IWorkbenchRename? rename = null)
        : base(new PlainListModel<BenchRow>(row => row.Key, row => row.Text))
    {
        ArgumentNullException.ThrowIfNull(recipes);
        _recipes = recipes;
        _rename = rename;
        _stamp = recipes.Stamp;
        Rebuild(null);
    }

    public event Action<string>? CraftCompleted;

    public event Action<string>? CraftRejected;

    public event Action<RenameRequest>? RenameRequested;

    public bool CanRenameSelected
    {
        get
        {
            if (_view != WorkbenchView.Targets || _rename is null || Rows.Selected is not { } row) return false;
            try
            {
                return _rename.CanRename(row.Key);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public WorkbenchView View => _view;

    public string Verb => string.IsNullOrWhiteSpace(_recipes.ApplyVerb) ? DefaultVerb : _recipes.ApplyVerb;

    public override string Title => _recipes.Title;

    public override string Header
    {
        get
        {
            var parts = new List<string>();
            if (_view >= WorkbenchView.Slots) parts.Add(Find(_recipes.Targets.Select(t => (t.Id, t.Name)), _targetId));
            if (_view >= WorkbenchView.Options) parts.Add(Find(_recipes.SlotsOf(_targetId).Select(s => (s.Id, s.Name)), _slotId));
            if (_view >= WorkbenchView.Confirm) parts.Add(Find(_recipes.OptionsOf(_targetId, _slotId).Select(o => (o.Id, o.Name)), _optionId));
            return string.Join(" > ", parts);
        }
    }

    public override IReadOnlyList<MenuHint> Hints => _view switch
    {
        WorkbenchView.Confirm => new[] { new MenuHint(MenuInputAction.Accept, Verb), new MenuHint(MenuInputAction.Back, "Cancel") },
        WorkbenchView.Targets => TargetHints(),
        _ => new[] { new MenuHint(MenuInputAction.Accept, "Select"), new MenuHint(MenuInputAction.Back, "Back") },
    };

    public override IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            var lines = new List<MenuTextLine>();
            if (Rows.RowCount == 0 && _view != WorkbenchView.Confirm)
            {
                lines.Add(new MenuTextLine(EmptyText, LineTone.Muted));
                return lines;
            }
            switch (_view)
            {
                case WorkbenchView.Targets:
                    if (_recipes.Targets.FirstOrDefault(t => t.Id == Rows.Selected?.Key) is { Description.Length: > 0 } target)
                        lines.Add(new MenuTextLine(target.Description));
                    break;
                case WorkbenchView.Slots:
                    if (_recipes.SlotsOf(_targetId).FirstOrDefault(s => s.Id == Rows.Selected?.Key) is { Current.Length: > 0 } slot)
                        lines.Add(new MenuTextLine("Now: " + slot.Current));
                    break;
                case WorkbenchView.Options:
                    AddOption(lines, Rows.Selected?.Key ?? string.Empty);
                    break;
                default:
                    lines.Add(new MenuTextLine(Verb + " " + Find(_recipes.OptionsOf(_targetId, _slotId).Select(o => (o.Id, o.Name)), _optionId) + "?",
                        LineTone.Heading));
                    AddRequirements(lines, _optionId);
                    break;
            }
            return lines;
        }
    }

    public static string StateWord(WorkbenchOptionState state) => state switch
    {
        WorkbenchOptionState.Installed => "Installed",
        WorkbenchOptionState.LackingMaterials => "Lacking materials",
        WorkbenchOptionState.PerkRequired => "Perk required",
        WorkbenchOptionState.Unavailable => "Unavailable",
        _ => string.Empty,
    };

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        if (_crafting) return true;
        switch (action)
        {
            case MenuInputAction.Accept:
                OnAccept();
                return true;
            case MenuInputAction.Back:
                OnBack();
                return true;
            case MenuInputAction.Favourite when CanRenameSelected:
                RequestRename();
                return true;
            default:
                return _view != WorkbenchView.Confirm && Rows.Handle(action);
        }
    }

    public override void Tick(double deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (IsFinished || _crafting) return;
        var stamp = _recipes.Stamp;
        if (stamp == _stamp) return;
        _stamp = stamp;
        Reconcile();
    }

    private IReadOnlyList<MenuHint> TargetHints()
    {
        var hints = new List<MenuHint> { new(MenuInputAction.Accept, "Select") };
        if (CanRenameSelected) hints.Add(new MenuHint(MenuInputAction.Favourite, "Rename"));
        hints.Add(new MenuHint(MenuInputAction.Back, "Close"));
        return hints;
    }

    private void RequestRename()
    {
        var handler = RenameRequested;
        if (_rename is null || handler is null || Rows.Selected is not { } row)
        {
            Sound("menu.blocked");
            return;
        }
        var targetId = row.Key;
        string current;
        int maxLength;
        try
        {
            current = _rename.CurrentName(targetId) ?? string.Empty;
            maxLength = Math.Max(1, _rename.MaxNameLength);
        }
        catch (Exception)
        {
            Say(FailedText);
            Sound("menu.blocked");
            return;
        }
        var rename = _rename;
        var options = new TextPromptOptions
        {
            MaxLength = maxLength,
            Trim = TrimMode.Ends,
            Validator = name =>
            {
                try
                {
                    return rename.Validate(name);
                }
                catch (Exception)
                {
                    return FailedText;
                }
            },
        };
        var done = false;
        Sound("menu.forward");
        handler(new RenameRequest(RenameTitle, RenamePrompt, new TextPromptModel(current, options), text =>
        {
            if (done) return;
            done = true;
            CompleteRename(targetId, current, text);
        }));
    }

    private void CompleteRename(string targetId, string current, string? text)
    {
        if (IsFinished || _rename is null || text is null) return;
        var name = text.Trim();
        if (name.Length == 0 || string.Equals(name, current, StringComparison.Ordinal)) return;
        if (_recipes.Targets.All(t => t.Id != targetId))
        {
            Say(ChangedText);
            Sound("menu.blocked");
            return;
        }
        ActionResult result;
        try
        {
            result = _rename.Rename(targetId, name);
        }
        catch (Exception)
        {
            result = new ActionResult(false, FailedText);
        }
        if (IsFinished) return;
        _stamp = _recipes.Stamp;
        if (result.Ok)
        {
            Reconcile();
            Say(RenamedText);
            Sound("menu.confirm");
            return;
        }
        Say(result.Reason.Length > 0 ? result.Reason : FailedText);
        Sound("menu.blocked");
    }

    private static string Find(IEnumerable<(string Id, string Name)> items, string id)
    {
        foreach (var item in items)
            if (item.Id == id) return item.Name;
        return id;
    }

    private string TextOf(WorkbenchOption option)
    {
        var word = StateWord(_recipes.StateOf(_targetId, _slotId, option.Id));
        return word.Length > 0 ? option.Name + " - " + word : option.Name;
    }

    private void Rebuild(string? selectKey)
    {
        IEnumerable<BenchRow> rows = _view switch
        {
            WorkbenchView.Targets => _recipes.Targets.Select(t => new BenchRow(t.Id, t.Name)),
            WorkbenchView.Slots => _recipes.SlotsOf(_targetId).Select(s => new BenchRow(s.Id, s.Name)),
            _ => _recipes.OptionsOf(_targetId, _slotId).Select(o => new BenchRow(o.Id, TextOf(o))),
        };
        Rows.ShowRows(rows.ToList(), selectKey);
        Rows.Summary = Header;
    }

    private void OnAccept()
    {
        switch (_view)
        {
            case WorkbenchView.Targets:
                if (Rows.Selected is not { } target) return;
                _targetId = target.Key;
                GoTo(WorkbenchView.Slots, null);
                return;
            case WorkbenchView.Slots:
                if (Rows.Selected is not { } slot) return;
                _slotId = slot.Key;
                GoTo(WorkbenchView.Options, null);
                return;
            case WorkbenchView.Options:
                if (Rows.Selected is not { } option) return;
                var state = _recipes.StateOf(_targetId, _slotId, option.Key);
                if (state != WorkbenchOptionState.Available)
                {
                    Say(StateWord(state) + ".");
                    Sound(state is WorkbenchOptionState.LackingMaterials or WorkbenchOptionState.PerkRequired ? "bench.build_fail" : "menu.blocked");
                    return;
                }
                _optionId = option.Key;
                GoTo(WorkbenchView.Confirm, null);
                return;
            default:
                Craft();
                return;
        }
    }

    private void OnBack()
    {
        switch (_view)
        {
            case WorkbenchView.Confirm:
                GoTo(WorkbenchView.Options, _optionId);
                return;
            case WorkbenchView.Options:
                GoTo(WorkbenchView.Slots, _slotId);
                return;
            case WorkbenchView.Slots:
                GoTo(WorkbenchView.Targets, _targetId);
                return;
            default:
                Finish(false);
                return;
        }
    }

    private void GoTo(WorkbenchView view, string? selectKey)
    {
        var forward = view > _view;
        _view = view;
        Sound(forward ? "menu.forward" : "menu.back");
        if (view == WorkbenchView.Confirm)
        {
            Rows.SetRows(Array.Empty<BenchRow>());
            Rows.Summary = Header;
            Raise();
            return;
        }
        Rebuild(selectKey);
    }

    private void Craft()
    {
        if (_recipes.StateOf(_targetId, _slotId, _optionId) != WorkbenchOptionState.Available)
        {
            Say(ChangedText);
            Sound("menu.blocked");
            GoTo(WorkbenchView.Options, _optionId);
            return;
        }
        var optionId = _optionId;
        _crafting = true;
        ActionResult result;
        try
        {
            result = _recipes.Craft(_targetId, _slotId, optionId);
        }
        finally
        {
            _crafting = false;
        }
        if (IsFinished) return;
        _stamp = _recipes.Stamp;
        if (result.Ok)
        {
            ReturnAfterCraft(optionId);
            Say(DoneText);
            Sound("menu.confirm");
            CraftCompleted?.Invoke(optionId);
            return;
        }
        var reason = result.Reason.Length > 0 ? result.Reason : FailedText;
        ReturnAfterCraft(optionId);
        Say(reason);
        Sound("menu.blocked");
        CraftRejected?.Invoke(reason);
    }

    private void ReturnAfterCraft(string optionId)
    {
        if (_recipes.Targets.All(t => t.Id != _targetId)) GoTo(WorkbenchView.Targets, _targetId);
        else if (_recipes.SlotsOf(_targetId).All(s => s.Id != _slotId)) GoTo(WorkbenchView.Slots, _slotId);
        else GoTo(WorkbenchView.Options, optionId);
    }

    private void Reconcile()
    {
        if (_view >= WorkbenchView.Slots && _recipes.Targets.All(t => t.Id != _targetId)) _view = WorkbenchView.Targets;
        if (_view >= WorkbenchView.Options && _recipes.SlotsOf(_targetId).All(s => s.Id != _slotId))
            _view = WorkbenchView.Slots;
        if (_view >= WorkbenchView.Confirm
            && (_recipes.OptionsOf(_targetId, _slotId).All(o => o.Id != _optionId)
                || _recipes.StateOf(_targetId, _slotId, _optionId) != WorkbenchOptionState.Available))
        {
            _view = WorkbenchView.Options;
            Say(ChangedText);
        }
        if (_view == WorkbenchView.Confirm)
        {
            Raise();
            return;
        }
        Rebuild(Rows.Selected?.Key);
    }

    private void AddOption(List<MenuTextLine> lines, string optionId)
    {
        var option = _recipes.OptionsOf(_targetId, _slotId).FirstOrDefault(o => o.Id == optionId);
        if (option is null) return;
        lines.Add(new MenuTextLine(option.Name, LineTone.Heading));
        var state = _recipes.StateOf(_targetId, _slotId, optionId);
        if (state != WorkbenchOptionState.Available) lines.Add(new MenuTextLine(StateWord(state), LineTone.Bad));
        if (option.Description.Length > 0) lines.Add(new MenuTextLine(option.Description));
        AddRequirements(lines, optionId);
    }

    private void AddRequirements(List<MenuTextLine> lines, string optionId)
    {
        var needs = _recipes.Requirements(_targetId, _slotId, optionId);
        if (needs.Count == 0) return;
        lines.Add(new MenuTextLine("Needs", LineTone.Heading));
        foreach (var need in needs)
        {
            var have = _recipes.AvailableCount(need.Id);
            lines.Add(new MenuTextLine(CountText.HaveNeed(need.Name, have, need.Needed), have < need.Needed ? LineTone.Bad : LineTone.Normal));
        }
    }
}
