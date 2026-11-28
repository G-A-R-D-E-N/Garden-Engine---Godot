using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.RemoteAccess;

public sealed record RemoteRow(string Id, string Text);

public sealed class RemoteAccessController : ListDetailController<RemoteRow>
{
    public const string CloseText = "Close";

    private static readonly RemoteRow CloseRow = new("close", CloseText);

    private readonly string _title;
    private string _status;
    private Action? _done;

    private RemoteAccessController(string title, string status, Action done)
        : base(new PlainListModel<RemoteRow>(row => row.Id, row => row.Text))
    {
        _title = title;
        _status = status;
        _done = done;
        Rows.SetRows(new[] { CloseRow });
        Rows.Accepted += _ => Close();
    }

    public override string Title => _title;

    public string Status => _status;

    public override IReadOnlyList<MenuHint> Hints => new[] { new MenuHint(MenuInputAction.Accept, CloseText) };

    public override IReadOnlyList<MenuTextLine> DetailLines => new[] { new MenuTextLine(_status) };

    public static RemoteAccessController? TryCreate(string? title, string? status, Action? done) =>
        string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(status) || done is null
            ? null
            : new RemoteAccessController(title, status, done);

    public bool SetStatus(string? status)
    {
        if (IsFinished || string.IsNullOrWhiteSpace(status) || status == _status) return false;
        _status = status;
        Raise();
        return true;
    }

    public void Close()
    {
        if (IsFinished) return;
        var done = _done;
        _done = null;
        Sound("menu.confirm");
        Finish(false);
        done?.Invoke();
    }

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        switch (action)
        {
            case MenuInputAction.Accept:
            case MenuInputAction.Back:
                Close();
                return true;
            default:
                return false;
        }
    }
}
