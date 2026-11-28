using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed class StatAllocationController : CharacterMenuController
{
    private enum Command { Undo, Redo, Reset, Preset, Rename, Confirm }

    private readonly StatAllocationModel _model;
    private readonly CharacterSheetModel? _sheet;
    private readonly StatDef[] _defs;
    private readonly CharacterNameModel? _name;
    private readonly string _introduction;
    private readonly List<Command> _commands = new();
    private int _presetIndex = -1;

    public StatAllocationController(StatAllocationModel model, CharacterSheetModel? sheet = null, string title = "Allocate points",
                                    string introduction = "", CharacterNameModel? name = null)
    {
        _model = model;
        _sheet = sheet;
        _name = name;
        _introduction = introduction;
        Title = title;
        _defs = model.Stats.Visible.ToArray();
        _commands.Add(Command.Undo);
        _commands.Add(Command.Redo);
        _commands.Add(Command.Reset);
        if (model.Presets.Count > 0) _commands.Add(Command.Preset);
        if (name is not null) _commands.Add(Command.Rename);
        _commands.Add(Command.Confirm);
        _model.Changed += SyncSheet;
        if (name is not null) name.Changed += Raise;
        SyncSheet();
    }

    public override string Title { get; }

    public CharacterNameModel? NameModel => _name;

    public override string Header => FormattableString.Invariant($"Points left: {_model.Remaining} of {_model.Pool}");

    public override int RowCount => _defs.Length + _commands.Count;

    public override IReadOnlyList<MenuTextLine> SideLines
    {
        get
        {
            var lines = new List<MenuTextLine>();
            lines.AddRange(NameLines());
            lines.AddRange(DescriptionLines());
            if (_sheet is null) return lines;
            lines.Add(new MenuTextLine("Effect of changes", LineTone.Heading));
            foreach (var row in _sheet.Rows)
            {
                if (!row.Changed)
                {
                    lines.Add(new MenuTextLine(FormattableString.Invariant($"{row.Label}: {row.Current:0.##}"), LineTone.Muted));
                    continue;
                }
                var tone = row.Change == SheetChange.Better ? LineTone.Good : LineTone.Bad;
                lines.Add(new MenuTextLine(FormattableString.Invariant($"{row.Label}: {row.Current:0.##}  >  {row.After:0.##}   ({row.Delta:+0.##;-0.##;0})"), tone));
            }
            return lines;
        }
    }

    public override IReadOnlyList<MenuHint> Hints
    {
        get
        {
            if (_name is { IsEditing: true })
                return new[] { new MenuHint(MenuInputAction.Accept, "Done"), new MenuHint(MenuInputAction.Back, "Cancel edit") };
            var hints = new List<MenuHint>
            {
                new(MenuInputAction.Accept, IsStat(SelectedIndex) ? "Confirm" : Label(SelectedIndex)),
                new(MenuInputAction.Back, "Cancel"),
                new(MenuInputAction.Sort, "Reset"),
            };
            if (_model.Presets.Count > 0)
            {
                hints.Add(new MenuHint(MenuInputAction.TabPrev, "Preset: " + PresetAt(Peek(-1)).Name));
                hints.Add(new MenuHint(MenuInputAction.TabNext, "Preset: " + PresetAt(Peek(1)).Name));
            }
            return hints;
        }
    }

    public override ValueRowKind Kind(int row) => IsStat(row) ? ValueRowKind.Stepper : ValueRowKind.Command;

    public override string Label(int row)
    {
        if (row < 0 || row >= RowCount) return string.Empty;
        if (IsStat(row)) return _defs[row].Label;
        return CommandAt(row) switch
        {
            Command.Undo => "Undo",
            Command.Redo => "Redo",
            Command.Reset => "Reset",
            Command.Preset => "Preset: " + PresetAt(Peek(1)).Name,
            Command.Rename => "Rename",
            _ => "Confirm",
        };
    }

    public override string ValueText(int row) =>
        IsStat(row) ? _model.Value(_defs[row].Id).ToString(CultureInfo.InvariantCulture) : string.Empty;

    public override double Fraction(int row)
    {
        if (!IsStat(row)) return -1;
        var def = _defs[row];
        return def.Max == def.Min ? 0 : (double)(_model.Value(def.Id) - def.Min) / (def.Max - def.Min);
    }

    public override bool Enabled(int row)
    {
        if (row < 0 || row >= RowCount) return false;
        if (IsStat(row)) return true;
        return CommandAt(row) switch
        {
            Command.Undo => _model.CanUndo,
            Command.Redo => _model.CanRedo,
            Command.Reset => _model.Spent > 0,
            _ => true,
        };
    }

    public override bool CanStep(int row, int direction)
    {
        if (row < 0 || row >= RowCount) return false;
        if (IsStat(row)) return direction > 0 ? _model.CanIncrement(_defs[row].Id) : _model.CanDecrement(_defs[row].Id);
        return CommandAt(row) == Command.Preset;
    }

    protected override bool UndoCore() => _model.Undo();

    protected override bool RedoCore() => _model.Redo();

    protected override bool SetFractionCore(int row, double fraction, bool coalesce) => false;

    protected override bool StepCore(int row, int direction)
    {
        if (!IsStat(row)) return CommandAt(row) == Command.Preset && ApplyPreset(direction);
        var id = _defs[row].Id;
        if (direction > 0)
        {
            var ok = _model.Increment(id);
            if (!ok && _model.Remaining == 0) Message = "No points left.";
            return ok;
        }
        return _model.Decrement(id);
    }

    protected override bool ActivateCore(int row)
    {
        if (IsStat(row)) return TryConfirm();
        switch (CommandAt(row))
        {
            case Command.Undo: return _model.Undo();
            case Command.Redo: return _model.Redo();
            case Command.Reset: return _model.Reset();
            case Command.Preset: return ApplyPreset(1);
            case Command.Rename: return _name is { } n && (n.IsEditing ? n.EndEdit() : n.BeginEdit());
            default: return TryConfirm();
        }
    }

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished || _name is not { IsEditing: true } name) return base.Handle(action);
        Message = string.Empty;
        switch (action)
        {
            case MenuInputAction.Accept: name.EndEdit(); break;
            case MenuInputAction.Back: name.CancelEdit(); break;
            case MenuInputAction.Left: name.Editor!.MoveCaret(-1); break;
            case MenuInputAction.Right: name.Editor!.MoveCaret(1); break;
        }
        return true;
    }

    protected override bool HandleExtra(MenuInputAction action)
    {
        if (action != MenuInputAction.Sort) return false;
        RunGlobal(() => _model.Reset());
        return true;
    }

    protected override bool OnTab(int delta)
    {
        if (_model.Presets.Count == 0) return false;
        ApplyPreset(delta);
        return true;
    }

    protected override void OnCancel()
    {
        _name?.CancelEdit();
        _model.Cancel();
    }

    private bool IsStat(int row) => row >= 0 && row < _defs.Length;

    private Command CommandAt(int row) => _commands[row - _defs.Length];

    private StatPreset PresetAt(int index) => _model.Presets[index];

    private int Peek(int delta)
    {
        var count = _model.Presets.Count;
        if (_presetIndex < 0) return delta > 0 ? 0 : count - 1;
        return ((_presetIndex + delta) % count + count) % count;
    }

    private bool ApplyPreset(int delta)
    {
        if (_model.Presets.Count == 0) return false;
        var index = Peek(delta);
        var name = PresetAt(index).Name;
        _presetIndex = index;
        if (_model.ApplyPreset(name)) return true;
        Message = "Preset " + name + " changes nothing or does not fit the pool.";
        return false;
    }

    private IEnumerable<MenuTextLine> NameLines()
    {
        if (_name is null) yield break;
        yield return new MenuTextLine("Name", LineTone.Heading);
        var shown = _name.IsEditing ? _name.EditText : _name.Name;
        yield return new MenuTextLine(shown.Length == 0 ? "(no name)" : shown, shown.Length == 0 ? LineTone.Muted : LineTone.Normal);
        if (_name.CurrentCheck.Message is { } problem) yield return new MenuTextLine(problem, LineTone.Bad);
    }

    private IEnumerable<MenuTextLine> DescriptionLines()
    {
        if (IsStat(SelectedIndex))
        {
            var def = _defs[SelectedIndex];
            if (def.Description.Length == 0) yield break;
            yield return new MenuTextLine(def.Label, LineTone.Heading);
            yield return new MenuTextLine(def.Description);
        }
        else if (_introduction.Length > 0)
        {
            yield return new MenuTextLine(_introduction);
        }
    }

    private bool TryConfirm()
    {
        _name?.EndEdit();
        if (_name is { } name && name.CurrentCheck is { IsValid: false, Message: { } problem })
        {
            Message = problem;
            return false;
        }
        switch (_model.TryConfirm())
        {
            case AllocationConfirm.Confirmed:
                Finish(true);
                return true;
            case AllocationConfirm.UnspentPoints:
                Message = FormattableString.Invariant($"Spend every point before confirming ({_model.Remaining} left).");
                return false;
            case AllocationConfirm.Invalid:
                Message = "The allocation is not valid.";
                return false;
            default:
                return false;
        }
    }

    private void SyncSheet() => _sheet?.SetPending(_model.Values);
}
