using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.Flow;

public enum PauseFlowState
{
    Closed,
    Pause,
    Settings,
    Load,
    Character,
    Appearance,
    Gallery,
    ConfirmQuit,
    Services,
}

public enum PauseAction
{
    Resume,
    LoadGame,
    Settings,
    Character,
    Appearance,
    Gallery,
    Quit,
    Services,
}

public sealed record PauseEntry(PauseAction Action, string Label, bool Enabled, string DisabledReason = "");

public sealed class PauseFlowModel
{
    public const string NoSaves = "No saved games";

    private readonly bool _includeGallery;
    private readonly bool _includeServices;
    private readonly bool _includeCharacter;
    private PauseEntry[] _entries = Array.Empty<PauseEntry>();

    public PauseFlowModel(bool includeGallery = false, bool includeServices = false, bool includeCharacter = true)
    {
        _includeGallery = includeGallery;
        _includeServices = includeServices;
        _includeCharacter = includeCharacter;
        Refresh(false);
    }

    public PauseFlowState State { get; private set; }

    public bool IsOpen => State != PauseFlowState.Closed;

    public bool QuitRequested { get; private set; }

    public IReadOnlyList<PauseEntry> Entries => _entries;

    public int SelectedIndex { get; private set; }

    public PauseEntry Selected => _entries[SelectedIndex];

    public bool Open(bool hasSaves)
    {
        if (State != PauseFlowState.Closed) return false;
        QuitRequested = false;
        State = PauseFlowState.Pause;
        Refresh(hasSaves);
        SelectedIndex = 0;
        return true;
    }

    public void Refresh(bool hasSaves)
    {
        var previous = _entries.Length == 0 ? (PauseAction?)null : Selected.Action;
        var entries = new List<PauseEntry>
        {
            new(PauseAction.Resume, "Resume", true),
            new(PauseAction.LoadGame, "Load Game", hasSaves, hasSaves ? "" : NoSaves),
            new(PauseAction.Settings, "Settings", true),
        };
        if (_includeCharacter)
        {
            entries.Add(new PauseEntry(PauseAction.Character, "Character", true));
            entries.Add(new PauseEntry(PauseAction.Appearance, "Appearance", true));
        }
        if (_includeServices) entries.Add(new PauseEntry(PauseAction.Services, "Mods and Setup", true));
        if (_includeGallery) entries.Add(new PauseEntry(PauseAction.Gallery, "UI Gallery", true));
        entries.Add(new PauseEntry(PauseAction.Quit, "Quit", true));
        _entries = entries.ToArray();
        var keep = previous is { } p ? Array.FindIndex(_entries, e => e.Action == p && e.Enabled) : -1;
        SelectedIndex = keep >= 0 ? keep : 0;
    }

    public void Move(CursorMove move)
    {
        switch (move)
        {
            case CursorMove.Up: Step(-1); break;
            case CursorMove.Down: Step(1); break;
            case CursorMove.Home or CursorMove.PageUp: SelectedIndex = Array.FindIndex(_entries, e => e.Enabled); break;
            case CursorMove.End or CursorMove.PageDown: SelectedIndex = Array.FindLastIndex(_entries, e => e.Enabled); break;
        }
    }

    public bool Select(PauseAction action)
    {
        var index = Array.FindIndex(_entries, e => e.Action == action && e.Enabled);
        if (index < 0) return false;
        SelectedIndex = index;
        return true;
    }

    public PauseEntry? Activate()
    {
        if (State != PauseFlowState.Pause || !Selected.Enabled) return null;
        var entry = Selected;
        State = entry.Action switch
        {
            PauseAction.Resume => PauseFlowState.Closed,
            PauseAction.LoadGame => PauseFlowState.Load,
            PauseAction.Settings => PauseFlowState.Settings,
            PauseAction.Character => PauseFlowState.Character,
            PauseAction.Appearance => PauseFlowState.Appearance,
            PauseAction.Gallery => PauseFlowState.Gallery,
            PauseAction.Services => PauseFlowState.Services,
            _ => PauseFlowState.ConfirmQuit,
        };
        return entry;
    }

    public bool Back()
    {
        switch (State)
        {
            case PauseFlowState.Pause:
                State = PauseFlowState.Closed;
                return true;
            case PauseFlowState.Settings or PauseFlowState.Load or PauseFlowState.Character or PauseFlowState.Appearance
                or PauseFlowState.Gallery or PauseFlowState.ConfirmQuit or PauseFlowState.Services:
                State = PauseFlowState.Pause;
                return true;
            default:
                return false;
        }
    }

    public bool LoadChosen()
    {
        if (State != PauseFlowState.Load) return false;
        State = PauseFlowState.Closed;
        return true;
    }

    public bool ConfirmQuit(bool confirmed)
    {
        if (State != PauseFlowState.ConfirmQuit) return false;
        QuitRequested = confirmed;
        State = confirmed ? PauseFlowState.Closed : PauseFlowState.Pause;
        return true;
    }

    public bool Close()
    {
        if (State == PauseFlowState.Closed) return false;
        State = PauseFlowState.Closed;
        return true;
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

public enum ModalChange
{
    None,
    Entered,
    Exited,
    ExitedRestoreCapture,
}

public sealed class ModalSession
{
    private bool _restoreCapture;

    public bool Active { get; private set; }

    public ModalChange Update(bool menusOpen, bool mouseCaptured)
    {
        if (menusOpen == Active) return ModalChange.None;
        Active = menusOpen;
        if (menusOpen)
        {
            _restoreCapture = mouseCaptured;
            return ModalChange.Entered;
        }
        var restore = _restoreCapture;
        _restoreCapture = false;
        return restore ? ModalChange.ExitedRestoreCapture : ModalChange.Exited;
    }
}
