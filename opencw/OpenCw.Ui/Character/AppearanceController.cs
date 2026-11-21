using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed class AppearanceController : CharacterMenuController
{
    private enum Command { Randomise, ResetCategory, Undo, Redo, Symmetric, Compare, SavePreset, LoadPreset, DeletePreset, Done }

    public const int MaxCreateAttempts = 16;

    private static readonly Command[] Commands = (Command[])Enum.GetValues(typeof(Command));

    private readonly AppearanceModel _model;
    private IReadOnlyList<AppearanceControl> _rows = Array.Empty<AppearanceControl>();
    private IReadOnlyList<string> _names = Array.Empty<string>();
    private int _tab;
    private int _preset;

    public AppearanceController(AppearanceModel model, string title = "Appearance")
    {
        _model = model;
        Title = title;
        RefreshPresets();
        RebuildRows();
    }

    public override string Title { get; }

    public AppearanceModel Model => _model;

    public string? Category => _model.Categories.Count > 0 ? _model.Categories[Math.Clamp(_tab, 0, _model.Categories.Count - 1)] : null;

    public IReadOnlyList<string> PresetNames => _names;

    public override IReadOnlyList<string> Tabs => _model.Categories;

    public override int TabIndex => _tab;

    public override int RowCount => _rows.Count + Commands.Length;

    public override string Header
    {
        get
        {
            if (_model.ShowOriginal) return "Showing the original look";
            var text = Category is null ? "Editing" : "Editing: " + Category;
            return _model.Symmetric ? text + " (symmetric)" : text;
        }
    }

    public override IReadOnlyList<MenuTextLine> SideLines
    {
        get
        {
            var lines = new List<MenuTextLine> { new("Changed from the original", LineTone.Heading) };
            var changed = _model.ChangedFromOriginal;
            if (changed.Count == 0)
            {
                lines.Add(new MenuTextLine("Nothing yet", LineTone.Muted));
                return lines;
            }
            var original = _model.OriginalValues;
            var current = _model.Values;
            foreach (var id in changed)
                lines.Add(new MenuTextLine(ControlLabel(id) + ": " + Describe(id, original[id]) + "  >  " + Describe(id, current[id])));
            return lines;
        }
    }

    public override IReadOnlyList<MenuHint> Hints
    {
        get
        {
            var hints = new List<MenuHint>
            {
                new(MenuInputAction.Accept, IsControl(SelectedIndex) ? "Done" : Label(SelectedIndex)),
                new(MenuInputAction.Back, "Cancel"),
                new(MenuInputAction.Sort, "Reset category"),
                new(MenuInputAction.Compare, _model.ShowOriginal ? "Show edit" : "Show original"),
                new(MenuInputAction.Favourite, "Randomise"),
            };
            if (Tabs.Count > 1)
            {
                hints.Add(new MenuHint(MenuInputAction.TabPrev, "Previous: " + Tabs[Wrap(_tab - 1)]));
                hints.Add(new MenuHint(MenuInputAction.TabNext, "Next: " + Tabs[Wrap(_tab + 1)]));
            }
            return hints;
        }
    }

    public void RefreshPresets()
    {
        _names = _model.PresetNames.ToArray();
        _preset = _names.Count == 0 ? 0 : Math.Clamp(_preset, 0, _names.Count - 1);
    }

    public override bool SelectTab(int index)
    {
        if (index < 0 || index >= Tabs.Count) return false;
        _tab = index;
        RebuildRows();
        Select(0);
        return true;
    }

    public override ValueRowKind Kind(int row) =>
        !IsControl(row) ? ValueRowKind.Command : _rows[row].IsDiscrete ? ValueRowKind.Stepper : ValueRowKind.Slider;

    public override string Label(int row)
    {
        if (row < 0 || row >= RowCount) return string.Empty;
        if (IsControl(row)) return _rows[row].Label;
        return CommandAt(row) switch
        {
            Command.Randomise => "Randomise",
            Command.ResetCategory => "Reset category",
            Command.Undo => "Undo",
            Command.Redo => "Redo",
            Command.Symmetric => "Symmetric edit",
            Command.Compare => "Compare with original (hold)",
            Command.SavePreset => "Save preset",
            Command.LoadPreset => "Load preset",
            Command.DeletePreset => "Delete preset",
            _ => "Done",
        };
    }

    public override string ValueText(int row)
    {
        if (row < 0 || row >= RowCount) return string.Empty;
        if (IsControl(row)) return Describe(_rows[row], _model.Displayed(_rows[row].Id));
        return CommandAt(row) switch
        {
            Command.Symmetric => _model.Symmetric ? "On" : "Off",
            Command.Compare => _model.ShowOriginal ? "Showing original" : "Showing edit",
            Command.LoadPreset => _names.Count == 0 ? "None saved" : _names[_preset],
            Command.DeletePreset => _names.Count == 0 ? "None saved" : _names[_preset],
            _ => string.Empty,
        };
    }

    public override double Fraction(int row)
    {
        if (!IsControl(row) || _rows[row].IsDiscrete) return -1;
        var control = _rows[row];
        return control.Max <= control.Min ? 0 : (_model.Displayed(control.Id) - control.Min) / (control.Max - control.Min);
    }

    public override bool Enabled(int row)
    {
        if (row < 0 || row >= RowCount) return false;
        if (IsControl(row)) return true;
        return CommandAt(row) switch
        {
            Command.Undo => _model.CanUndo,
            Command.Redo => _model.CanRedo,
            Command.LoadPreset or Command.DeletePreset => _names.Count > 0,
            _ => true,
        };
    }

    public override bool CanStep(int row, int direction)
    {
        if (row < 0 || row >= RowCount) return false;
        if (!IsControl(row))
            return CommandAt(row) is Command.Symmetric or Command.Compare or Command.LoadPreset or Command.DeletePreset;
        var control = _rows[row];
        var value = _model.Get(control.Id);
        return direction > 0 ? value < control.Max : value > control.Min;
    }

    public override bool HoldsOnPress(int row) => !IsControl(row) && row < RowCount && CommandAt(row) == Command.Compare;

    public override void EndDrag() => _model.EndCoalesce();

    protected override bool UndoCore() => _model.Undo();

    protected override bool RedoCore() => _model.Redo();

    protected override bool HoldCore(int row, bool down)
    {
        if (!HoldsOnPress(row)) return false;
        _model.ShowOriginal = down;
        return true;
    }

    protected override bool SetFractionCore(int row, double fraction, bool coalesce)
    {
        if (!IsControl(row) || _rows[row].IsDiscrete) return false;
        var control = _rows[row];
        return _model.Set(control.Id, control.Min + fraction * (control.Max - control.Min), coalesce);
    }

    protected override bool StepCore(int row, int direction)
    {
        if (IsControl(row))
        {
            var control = _rows[row];
            var amount = control.IsDiscrete ? 1 : control.Step > 0 ? control.Step : (control.Max - control.Min) / 20;
            return _model.Set(control.Id, _model.Get(control.Id) + direction * amount, !control.IsDiscrete);
        }
        switch (CommandAt(row))
        {
            case Command.Symmetric: _model.Symmetric = !_model.Symmetric; return true;
            case Command.Compare: _model.ShowOriginal = !_model.ShowOriginal; return true;
            case Command.LoadPreset:
            case Command.DeletePreset:
                if (_names.Count == 0) return false;
                _preset = ((_preset + direction) % _names.Count + _names.Count) % _names.Count;
                return true;
            default: return false;
        }
    }

    protected override bool ActivateCore(int row)
    {
        if (IsControl(row))
        {
            Finish(true);
            return true;
        }
        switch (CommandAt(row))
        {
            case Command.Randomise: return Randomise();
            case Command.ResetCategory: return _model.Reset(Category);
            case Command.Undo: return _model.Undo();
            case Command.Redo: return _model.Redo();
            case Command.Symmetric: _model.Symmetric = !_model.Symmetric; return true;
            case Command.Compare: _model.ShowOriginal = !_model.ShowOriginal; return true;
            case Command.SavePreset: return SavePreset();
            case Command.LoadPreset: return LoadPreset();
            case Command.DeletePreset: return DeletePreset();
            default:
                Finish(true);
                return true;
        }
    }

    protected override bool HandleExtra(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Compare: RunGlobal(() => { _model.ShowOriginal = !_model.ShowOriginal; return true; }); return true;
            case MenuInputAction.Favourite: RunGlobal(Randomise); return true;
            case MenuInputAction.Sort: RunGlobal(() => _model.Reset(Category)); return true;
            default: return false;
        }
    }

    protected override void OnCancel() => _model.RevertToOriginal();

    private bool IsControl(int row) => row >= 0 && row < _rows.Count;

    private Command CommandAt(int row) => Commands[row - _rows.Count];

    private int Wrap(int index) => ((index % Tabs.Count) + Tabs.Count) % Tabs.Count;

    private void RebuildRows() => _rows = Category is null ? Array.Empty<AppearanceControl>() : _model.ControlsIn(Category);

    private bool Randomise()
    {
        var changed = _model.Randomise(Category);
        Message = changed ? "Randomised." : "Nothing changed.";
        return changed;
    }

    private bool SavePreset()
    {
        string name;
        var attempt = 0;
        do
        {
            RefreshPresets();
            name = NextPresetName();
            if (_model.CreatePreset(name)) break;
            RefreshPresets();
            if (!_names.Contains(name, StringComparer.OrdinalIgnoreCase) || ++attempt >= MaxCreateAttempts)
            {
                Message = "Could not save the preset.";
                return false;
            }
        }
        while (true);
        RefreshPresets();
        _preset = Math.Max(0, _names.ToList().IndexOf(name));
        Message = "Saved " + name + ".";
        return true;
    }

    private bool LoadPreset()
    {
        if (_names.Count == 0)
        {
            Message = "No saved presets.";
            return false;
        }
        var name = _names[_preset];
        var ok = _model.LoadPreset(name);
        Message = ok ? "Loaded " + name + "." : "Could not load " + name + ".";
        return ok;
    }

    private bool DeletePreset()
    {
        if (_names.Count == 0)
        {
            Message = "No saved presets.";
            return false;
        }
        var name = _names[_preset];
        var ok = _model.DeletePreset(name);
        RefreshPresets();
        Message = ok ? "Deleted " + name + "." : "Could not delete " + name + ".";
        return ok;
    }

    private string NextPresetName()
    {
        for (var n = 1; ; n++)
        {
            var name = "Look " + n.ToString(CultureInfo.InvariantCulture);
            if (!_names.Contains(name, StringComparer.OrdinalIgnoreCase)) return name;
        }
    }

    private string ControlLabel(string id) => _model.Controls.Visible.First(c => c.Id == id).Label;

    private string Describe(string id, double value) => Describe(_model.Controls.Visible.First(c => c.Id == id), value);

    private static string Describe(AppearanceControl control, double value)
    {
        if (control.Options is { } options && options.Count > 0)
            return options[Math.Clamp((int)Math.Round(value), 0, options.Count - 1)];
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
