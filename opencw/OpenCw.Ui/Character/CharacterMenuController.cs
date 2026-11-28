using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Character;

public enum ValueRowKind { Stepper, Slider, Command }

public enum LineTone { Normal, Heading, Good, Bad, Muted }

public sealed record MenuTextLine(string Text, LineTone Tone = LineTone.Normal);

public interface IValueRows
{
    event Action? Changed;

    int RowCount { get; }

    int SelectedIndex { get; }

    ValueRowKind Kind(int row);

    string Label(int row);

    string ValueText(int row);

    double Fraction(int row);

    bool Enabled(int row);

    bool CanStep(int row, int direction);

    bool HoldsOnPress(int row);

    bool Select(int row);

    bool Step(int row, int direction);

    bool SetFraction(int row, double fraction, bool coalesce);

    bool Activate(int row);

    bool Hold(int row, bool down);

    void EndDrag();
}

public abstract class CharacterMenuController : IValueRows
{
    private int _selected;

    public event Action? Changed;

    public event Action<bool>? Finished;

    public abstract string Title { get; }

    public abstract string Header { get; }

    public abstract IReadOnlyList<MenuHint> Hints { get; }

    public abstract int RowCount { get; }

    public virtual string Footer => "PgUp or LT: undo    PgDn or RT: redo";

    public virtual IReadOnlyList<MenuTextLine> SideLines => Array.Empty<MenuTextLine>();

    public virtual IReadOnlyList<string> Tabs => Array.Empty<string>();

    public virtual int TabIndex => 0;

    public string Message { get; protected set; } = string.Empty;

    public bool IsFinished { get; private set; }

    public int SelectedIndex => RowCount == 0 ? -1 : Math.Clamp(_selected, 0, RowCount - 1);

    public abstract ValueRowKind Kind(int row);

    public abstract string Label(int row);

    public abstract string ValueText(int row);

    public abstract double Fraction(int row);

    public abstract bool Enabled(int row);

    public abstract bool CanStep(int row, int direction);

    public virtual bool HoldsOnPress(int row) => false;

    public bool Select(int row)
    {
        if (IsFinished || !InRange(row)) return false;
        if (row != SelectedIndex)
        {
            _selected = row;
            Raise();
        }
        return true;
    }

    public bool Step(int row, int direction) => Run(row, () => StepCore(row, direction));

    public bool SetFraction(int row, double fraction, bool coalesce) =>
        Run(row, () => SetFractionCore(row, Math.Clamp(fraction, 0, 1), coalesce));

    public bool Activate(int row) => Run(row, () => ActivateCore(row));

    public bool Hold(int row, bool down) => Run(row, () => HoldCore(row, down));

    public virtual void EndDrag()
    {
    }

    public virtual bool SelectTab(int index) => false;

    public virtual bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        switch (action)
        {
            case MenuInputAction.Up: Move(-1); return true;
            case MenuInputAction.Down: Move(1); return true;
            case MenuInputAction.Left: Step(SelectedIndex, -1); return true;
            case MenuInputAction.Right: Step(SelectedIndex, 1); return true;
            case MenuInputAction.Accept: Activate(SelectedIndex); return true;
            case MenuInputAction.Back: Cancel(); return true;
            case MenuInputAction.PageUp: RunGlobal(UndoCore); return true;
            case MenuInputAction.PageDown: RunGlobal(RedoCore); return true;
            case MenuInputAction.TabPrev: return Tab(-1);
            case MenuInputAction.TabNext: return Tab(1);
            default: return HandleExtra(action);
        }
    }

    public void Cancel()
    {
        if (IsFinished) return;
        OnCancel();
        Finish(false);
    }

    protected abstract bool UndoCore();

    protected abstract bool RedoCore();

    protected abstract bool StepCore(int row, int direction);

    protected abstract bool SetFractionCore(int row, double fraction, bool coalesce);

    protected abstract bool ActivateCore(int row);

    protected virtual bool HoldCore(int row, bool down) => false;

    protected virtual bool HandleExtra(MenuInputAction action) => false;

    protected virtual void OnCancel()
    {
    }

    protected bool InRange(int row) => row >= 0 && row < RowCount;

    protected void Raise() => Changed?.Invoke();

    protected void Finish(bool confirmed)
    {
        if (IsFinished) return;
        IsFinished = true;
        Finished?.Invoke(confirmed);
        Raise();
    }

    protected virtual bool OnTab(int delta)
    {
        var count = Tabs.Count;
        if (count < 2) return false;
        SelectTab(((TabIndex + delta) % count + count) % count);
        return true;
    }

    protected void RunGlobal(Func<bool> action)
    {
        Message = string.Empty;
        action();
        Raise();
    }

    private bool Run(int row, Func<bool> action)
    {
        if (IsFinished || !InRange(row)) return false;
        Message = string.Empty;
        var result = action();
        Raise();
        return result;
    }

    private bool Tab(int delta)
    {
        Message = string.Empty;
        var handled = OnTab(delta);
        if (handled) Raise();
        return handled;
    }

    private void Move(int delta)
    {
        if (RowCount == 0) return;
        _selected = ((SelectedIndex + delta) % RowCount + RowCount) % RowCount;
        Raise();
    }
}
