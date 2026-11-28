using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Character;

namespace OpenCommonwealth.Services.Ui.Menus;

public sealed record ActionResult(bool Ok, string Reason = "");

public interface IListDetailController
{
    event Action? Changed;

    event Action<bool>? Finished;

    event Action<string>? SoundRequested;

    string Title { get; }

    string Header { get; }

    string Message { get; }

    IListMenuView List { get; }

    IReadOnlyList<MenuTextLine> DetailLines { get; }

    IReadOnlyList<MenuHint> Hints { get; }

    bool IsFinished { get; }

    bool Handle(MenuInputAction action);

    void Tick(double deltaSeconds);
}

public abstract class ListDetailController<TRow> : IListDetailController
{
    public const double MessageSeconds = 3.0;

    private static readonly IReadOnlyList<MenuTextLine> NoLines = Array.Empty<MenuTextLine>();

    private string _message = string.Empty;
    private double _messageLeft;

    protected ListDetailController(PlainListModel<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Rows = rows;
        Rows.Changed += Raise;
    }

    public event Action? Changed;

    public event Action<bool>? Finished;

    public event Action<string>? SoundRequested;

    public PlainListModel<TRow> Rows { get; }

    public IListMenuView List => Rows;

    public virtual string Title => string.Empty;

    public virtual string Header => string.Empty;

    public virtual string Message => _message;

    public virtual IReadOnlyList<MenuTextLine> DetailLines => NoLines;

    public abstract IReadOnlyList<MenuHint> Hints { get; }

    public bool IsFinished { get; private set; }

    public bool IsConfirmed { get; private set; }

    public abstract bool Handle(MenuInputAction action);

    public virtual void Tick(double deltaSeconds)
    {
        if (_messageLeft <= 0 || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        _messageLeft -= deltaSeconds;
        if (_messageLeft > 0) return;
        _messageLeft = 0;
        _message = string.Empty;
        Raise();
    }

    protected void Say(string text)
    {
        _message = text;
        _messageLeft = text.Length > 0 ? MessageSeconds : 0;
        Raise();
    }

    protected void Sound(string name) => SoundRequested?.Invoke(name);

    protected void Raise() => Changed?.Invoke();

    protected void Finish(bool confirmed)
    {
        if (IsFinished) return;
        IsFinished = true;
        IsConfirmed = confirmed;
        Finished?.Invoke(confirmed);
        Raise();
    }
}
