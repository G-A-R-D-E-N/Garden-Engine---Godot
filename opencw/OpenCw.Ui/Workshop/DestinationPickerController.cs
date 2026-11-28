using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Workshop;

public sealed record DestinationEntry(string Id, string Text, bool Disabled = false, string Reason = "");

public sealed class DestinationPickerController : ListDetailController<DestinationEntry>
{
    public const string EmptyText = "No destinations available.";
    public const string NotAvailableText = "Not available.";
    public const string FailedText = "That didn't work.";

    private readonly string _header;
    private readonly string _promptTemplate;
    private readonly Func<bool> _isValid;
    private readonly Func<string, ActionResult> _commit;
    private bool _confirming;
    private bool _committing;

    private DestinationPickerController(string header, string promptTemplate, IReadOnlyList<DestinationEntry> entries,
                                        string? highlightedId, Func<bool> isValid, Func<string, ActionResult> commit)
        : base(new PlainListModel<DestinationEntry>(entry => entry.Id, entry => entry.Text))
    {
        _header = header;
        _promptTemplate = promptTemplate;
        _isValid = isValid;
        _commit = commit;
        Rows.SetRows(entries, highlightedId);
        if (Rows.RowCount > 0 && !Rows.HasSelection) Rows.Select(0);
    }

    public bool IsConfirming => _confirming;

    public DestinationEntry? Selected => Rows.Selected;

    public override string Title => _header;

    public override string Header => _confirming && Selected is { } entry ? Prompt(entry) : string.Empty;

    public override IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            if (Rows.RowCount == 0) return new[] { new MenuTextLine(EmptyText, LineTone.Muted) };
            if (_confirming && Selected is { } confirming)
                return new[] { new MenuTextLine(Prompt(confirming), LineTone.Heading) };
            if (Selected is { } entry && entry.Disabled)
                return new[] { new MenuTextLine(entry.Reason.Length > 0 ? entry.Reason : NotAvailableText, LineTone.Bad) };
            return Array.Empty<MenuTextLine>();
        }
    }

    public override IReadOnlyList<MenuHint> Hints =>
        _confirming
            ? new[] { new MenuHint(MenuInputAction.Accept, "Confirm"), new MenuHint(MenuInputAction.Back, "Cancel") }
            : new[] { new MenuHint(MenuInputAction.Accept, "Select"), new MenuHint(MenuInputAction.Back, "Close") };

    public static DestinationPickerController? TryCreate(string header, string promptTemplate,
                                                         IReadOnlyList<DestinationEntry>? entries, string? highlightedId,
                                                         Func<bool>? isValid, Func<string, ActionResult>? commit)
    {
        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(promptTemplate)
            || entries is null || isValid is null || commit is null)
            return null;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Text)
                || !ids.Add(entry.Id))
                return null;
        }
        if (!isValid()) return null;
        return new DestinationPickerController(header, promptTemplate, entries.ToArray(), highlightedId, isValid, commit);
    }

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        if (_committing) return true;
        if (_confirming)
        {
            if (action == MenuInputAction.Accept) Commit();
            else if (action == MenuInputAction.Back) SetConfirming(false);
            return true;
        }
        switch (action)
        {
            case MenuInputAction.Accept:
                Accept();
                return true;
            case MenuInputAction.Back:
                Finish(false);
                return true;
            default:
                return Rows.Handle(action);
        }
    }

    public override void Tick(double deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (!IsFinished && !_committing && !_isValid()) Finish(false);
    }

    private void Accept()
    {
        if (Rows.Selected is not { } entry) return;
        if (entry.Disabled)
        {
            Say(entry.Reason.Length > 0 ? entry.Reason : NotAvailableText);
            Sound("menu.blocked");
            return;
        }
        Sound("menu.forward");
        SetConfirming(true);
    }

    private void Commit()
    {
        if (Rows.Selected is not { } entry) return;
        _committing = true;
        ActionResult result;
        try
        {
            result = _commit(entry.Id);
        }
        finally
        {
            _committing = false;
        }
        if (IsFinished) return;
        if (result.Ok)
        {
            Finish(true);
            return;
        }
        _confirming = false;
        Say(result.Reason.Length > 0 ? result.Reason : FailedText);
        Sound("menu.blocked");
    }

    private void SetConfirming(bool on)
    {
        if (_confirming == on) return;
        _confirming = on;
        Raise();
    }

    private string Prompt(DestinationEntry entry) => _promptTemplate.Replace("{name}", entry.Text, StringComparison.Ordinal);
}
