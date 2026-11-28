using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public enum MainMenuAction
{
    NewGame,
    Continue,
    Load,
    Settings,
    Extras,
    Quit,
    Credits,
    Save,
    Quicksave,
}

public enum MainMenuMode
{
    Title,
    Pause,
}

public sealed record MainMenuEntry(MainMenuAction Action, string Label, bool Enabled, string DisabledReason = "",
                                   bool NeedsConfirmation = false, string ConfirmPrompt = "");

public sealed class MainMenuModel
{
    private const string NoSaves = "No saved games";

    private readonly ISaveSource _saves;
    private readonly string? _extrasLabel;
    private readonly bool _showCredits;
    private readonly ICanSaveQuery? _canSave;
    private readonly ISaveWriter? _writer;
    private MainMenuEntry[] _entries = Array.Empty<MainMenuEntry>();

    public MainMenuModel(ISaveSource saves, string? extrasLabel = null, bool showCredits = false,
                         MainMenuMode mode = MainMenuMode.Title, ICanSaveQuery? canSave = null, ISaveWriter? writer = null)
    {
        _saves = saves;
        _extrasLabel = extrasLabel;
        _showCredits = showCredits;
        Mode = mode;
        _canSave = canSave;
        _writer = writer;
        Refresh();
    }

    public MainMenuMode Mode { get; }

    public bool ResumesOnBack => Mode == MainMenuMode.Pause;

    public string Notice { get; private set; } = string.Empty;

    public IReadOnlyList<MainMenuEntry> Entries => _entries;

    public SaveSlotInfo? LastSave { get; private set; }

    public int SelectedIndex { get; private set; }

    public MainMenuEntry Selected => _entries[SelectedIndex];

    public void Refresh()
    {
        var saves = _saves.List();
        LastSave = saves.OrderByDescending(s => s.Timestamp).FirstOrDefault();
        var any = saves.Count > 0;
        var entries = Mode == MainMenuMode.Pause ? PauseEntries(any) : TitleEntries(any);

        var previous = _entries.Length == 0 ? (MainMenuAction?)null : Selected.Action;
        _entries = entries.ToArray();
        var keep = previous is { } p ? Array.FindIndex(_entries, e => e.Action == p && e.Enabled) : -1;
        SelectedIndex = keep >= 0 ? keep : DefaultIndex();
    }

    public ActionResult Quicksave()
    {
        Notice = string.Empty;
        var reason = string.Empty;
        ActionResult result;
        if (_writer is null)
            result = new ActionResult(false, SaveListController.FailedText);
        else if (_canSave is null || !_canSave.CanSave(out reason))
            result = new ActionResult(false, string.IsNullOrEmpty(reason) ? SaveListController.CannotSaveText : reason);
        else
            result = Write(_writer);
        if (!result.Ok) Notice = result.Reason.Length > 0 ? result.Reason : SaveListController.FailedText;
        Refresh();
        return result;
    }

    public void Move(CursorMove move)
    {
        Notice = string.Empty;
        switch (move)
        {
            case CursorMove.Up: Step(-1); break;
            case CursorMove.Down: Step(1); break;
            case CursorMove.Home or CursorMove.PageUp: SelectedIndex = Array.FindIndex(_entries, e => e.Enabled); break;
            case CursorMove.End or CursorMove.PageDown: SelectedIndex = Array.FindLastIndex(_entries, e => e.Enabled); break;
        }
    }

    public bool Select(MainMenuAction action)
    {
        Notice = string.Empty;
        var index = Array.FindIndex(_entries, e => e.Action == action && e.Enabled);
        if (index < 0) return false;
        SelectedIndex = index;
        return true;
    }

    public MainMenuEntry? Activate() => Selected.Enabled ? Selected : null;

    private static ActionResult Write(ISaveWriter writer)
    {
        try
        {
            return writer.Quicksave();
        }
        catch (Exception ex)
        {
            return new ActionResult(false, ex.Message);
        }
    }

    private List<MainMenuEntry> TitleEntries(bool any)
    {
        var entries = new List<MainMenuEntry>
        {
            new(MainMenuAction.NewGame, "New Game", true, "", any, "Start a new game? Your saved games are kept."),
            new(MainMenuAction.Continue, "Continue", LastSave is not null, LastSave is null ? NoSaves : "", LastSave is not null,
                LastSave is null ? "" : "Continue from \"" + LastSave.Name + "\"? " + SaveSlotText.Summary(LastSave)),
            new(MainMenuAction.Load, "Load", any, any ? "" : NoSaves),
            new(MainMenuAction.Settings, "Settings", true),
        };
        if (_extrasLabel is not null) entries.Add(new(MainMenuAction.Extras, _extrasLabel, true));
        if (_showCredits) entries.Add(new(MainMenuAction.Credits, "Credits", true));
        entries.Add(new(MainMenuAction.Quit, "Quit", true, NeedsConfirmation: true, ConfirmPrompt: "Quit to the desktop?"));
        return entries;
    }

    private List<MainMenuEntry> PauseEntries(bool any)
    {
        var reason = string.Empty;
        var canSave = _canSave is not null && _canSave.CanSave(out reason);
        var why = canSave ? string.Empty : reason ?? string.Empty;
        return new List<MainMenuEntry>
        {
            new(MainMenuAction.Quicksave, "Quicksave", canSave, why),
            new(MainMenuAction.Save, "Save", canSave, why),
            new(MainMenuAction.Load, "Load", any, any ? "" : NoSaves),
            new(MainMenuAction.Settings, "Settings", true),
            new(MainMenuAction.Quit, "Exit", true, NeedsConfirmation: true, ConfirmPrompt: "Exit to the title screen?"),
        };
    }

    private int DefaultIndex()
    {
        var preferred = Mode == MainMenuMode.Pause ? MainMenuAction.Save : MainMenuAction.Continue;
        var index = Array.FindIndex(_entries, e => e.Action == preferred && e.Enabled);
        if (index < 0) index = Array.FindIndex(_entries, e => e.Enabled);
        return index >= 0 ? index : 0;
    }

    private void Step(int delta)
    {
        for (var i = 1; i <= _entries.Length; i++)
        {
            var index = ((SelectedIndex + delta * i) % _entries.Length + _entries.Length) % _entries.Length;
            if (!_entries[index].Enabled) continue;
            SelectedIndex = index;
            return;
        }
    }
}
