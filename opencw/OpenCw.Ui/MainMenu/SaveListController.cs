using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public sealed record SaveRow(string Key, string Text, SaveSlotInfo? Slot);

public sealed class SaveListController : ListDetailController<SaveRow>
{
    public const string NewSaveText = "[NEW SAVE]";
    public const string NewSaveKey = "\u0001new";
    public const string SavedText = "Saved.";
    public const string DeletedText = "Deleted.";
    public const string FailedText = "That didn't work.";
    public const string DeleteFailedText = "Could not delete that save.";
    public const string CannotSaveText = "You can't save right now.";

    private enum Confirming
    {
        None,
        Overwrite,
        Delete,
    }

    private readonly ISaveSource _saves;
    private readonly ISaveWriter _writer;
    private readonly ICanSaveQuery? _canSave;
    private Confirming _confirming;
    private SaveSlotInfo? _pending;
    private bool _busy;

    public SaveListController(ISaveSource saves, ISaveWriter writer, ICanSaveQuery? canSave)
        : base(new PlainListModel<SaveRow>(row => row.Key, row => row.Text))
    {
        ArgumentNullException.ThrowIfNull(saves);
        ArgumentNullException.ThrowIfNull(writer);
        _saves = saves;
        _writer = writer;
        _canSave = canSave;
        Rebuild(true);
        Rows.Accepted += _ => OnAccept();
    }

    public bool IsConfirming => _confirming != Confirming.None;

    public override string Title => "Save game";

    public override string Header => Rows.RowCount > 0 ? (Rows.RowCount - 1) + (Rows.RowCount == 2 ? " saved game" : " saved games") : string.Empty;

    public override IReadOnlyList<MenuHint> Hints
    {
        get
        {
            if (IsConfirming)
                return new[] { new MenuHint(MenuInputAction.Accept, "Confirm"), new MenuHint(MenuInputAction.Back, "Cancel") };
            var hints = new List<MenuHint> { new(MenuInputAction.Accept, "Save") };
            if (Rows.Selected?.Slot is not null) hints.Add(new MenuHint(MenuInputAction.Compare, "Delete"));
            hints.Add(new MenuHint(MenuInputAction.Back, "Back"));
            return hints;
        }
    }

    public override IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            if (_confirming != Confirming.None && _pending is { } pending)
                return new[]
                {
                    new MenuTextLine((_confirming == Confirming.Delete ? "Delete \"" : "Overwrite \"") + pending.Name + "\"?", LineTone.Heading),
                };
            var lines = new List<MenuTextLine>();
            if (Rows.Selected is not { } row) return lines;
            if (row.Slot is null)
            {
                lines.Add(new MenuTextLine("Writes a new save.", LineTone.Muted));
                if (!Allowed(out var reason) && reason.Length > 0) lines.Add(new MenuTextLine(reason, LineTone.Bad));
                return lines;
            }
            lines.Add(new MenuTextLine(SaveSlotText.CharacterName(row.Slot), LineTone.Heading));
            lines.Add(new MenuTextLine(SaveSlotText.Summary(row.Slot)));
            lines.Add(new MenuTextLine(SaveSlotText.Timestamp(row.Slot.Timestamp), LineTone.Muted));
            return lines;
        }
    }

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        if (_busy) return true;
        if (IsConfirming) return HandleConfirm(action);
        switch (action)
        {
            case MenuInputAction.Accept:
                OnAccept();
                return true;
            case MenuInputAction.Compare:
                RequestDelete();
                return true;
            case MenuInputAction.Back:
                Finish(false);
                return true;
            default:
                return Rows.Handle(action);
        }
    }

    private bool Allowed(out string reason)
    {
        reason = string.Empty;
        if (_canSave is null) return false;
        var allowed = _canSave.CanSave(out var text);
        reason = allowed ? string.Empty : text ?? string.Empty;
        return allowed;
    }

    private void Rebuild(bool reset)
    {
        var rows = new List<SaveRow> { new(NewSaveKey, NewSaveText, null) };
        rows.AddRange(_saves.List()
            .OrderByDescending(slot => slot.Timestamp)
            .ThenBy(slot => slot.Id, StringComparer.Ordinal)
            .Select(slot => new SaveRow(slot.Id, SaveSlotText.RowText(slot), slot)));
        if (reset) Rows.ReplaceRows(rows);
        else Rows.SetRows(rows);
    }

    private void Blocked(string text)
    {
        Say(text);
        Sound("menu.blocked");
    }

    private bool CheckAllowed()
    {
        if (Allowed(out var reason)) return true;
        if (reason.Length > 0) Blocked(reason);
        else Sound("menu.blocked");
        return false;
    }

    private void OnAccept()
    {
        if (IsFinished || IsConfirming || _busy || Rows.Selected is not { } row || !CheckAllowed()) return;
        if (row.Slot is null)
        {
            Write(_writer.SaveNew);
            return;
        }
        Ask(Confirming.Overwrite, row.Slot);
    }

    private void RequestDelete()
    {
        if (Rows.Selected?.Slot is not { } slot)
        {
            Sound("menu.blocked");
            return;
        }
        Ask(Confirming.Delete, slot);
    }

    private void Ask(Confirming kind, SaveSlotInfo slot)
    {
        _confirming = kind;
        _pending = slot;
        Sound("menu.forward");
        Raise();
    }

    private bool HandleConfirm(MenuInputAction action)
    {
        if (action == MenuInputAction.Back)
        {
            Clear();
            Sound("menu.back");
        }
        else if (action == MenuInputAction.Accept)
        {
            var kind = _confirming;
            var slot = _pending;
            Clear();
            if (slot is null) return true;
            if (kind == Confirming.Delete) Delete(slot);
            else if (CheckAllowed()) Write(() => _writer.Overwrite(slot.Id));
        }
        return true;
    }

    private void Clear()
    {
        _confirming = Confirming.None;
        _pending = null;
        Raise();
    }

    private void Delete(SaveSlotInfo slot)
    {
        if (!_saves.Delete(slot.Id))
        {
            Blocked(DeleteFailedText);
            return;
        }
        Rebuild(false);
        Say(DeletedText);
    }

    private void Write(Func<ActionResult> write)
    {
        _busy = true;
        ActionResult result;
        try
        {
            result = write();
        }
        catch (Exception ex)
        {
            result = new ActionResult(false, ex.Message);
        }
        finally
        {
            _busy = false;
        }
        if (IsFinished) return;
        if (!result.Ok)
        {
            Blocked(result.Reason.Length > 0 ? result.Reason : FailedText);
            return;
        }
        Say(SavedText);
        Sound("menu.confirm");
        Finish(true);
    }
}
