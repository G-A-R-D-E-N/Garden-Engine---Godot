using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Service;

public enum ProfilePending
{
    None,
    Delete,
    Switch,
}

public enum NameRequestKind
{
    Create,
    Rename,
}

public sealed record NameRequest(NameRequestKind Kind, string Initial);

public sealed record ConfirmRequest(string Title, string Body, string ConfirmLabel);

public sealed class ProfilePickerModel
{
    public const string SwitchUnavailable = "Profiles cannot be switched from here.";

    private readonly IProfileStore _store;
    private readonly bool _inGame;
    private readonly List<ProfileInfo> _rows = new();
    private readonly ListMenuModel<ProfileInfo> _list;
    private string _previousId = string.Empty;
    private string? _renameId;

    public ProfilePickerModel(IProfileStore store, bool inGame = false)
    {
        _store = store;
        _inGame = inGame;
        _list = new ListMenuModel<ProfileInfo>(_rows, p => p.Id, p => p.Name);
        View = new ServiceListView<ProfileInfo>(_list, RowText) { Extra = Handle, HintsOf = Hints, SummaryText = () => Summary };
        Reload();
    }

    public event Action<NameRequest>? NameRequested;

    public event Action<ConfirmRequest>? ConfirmRequested;

    public event Action<ProfileInfo>? ProfileChosen;

    public ServiceListView<ProfileInfo> View { get; }

    public bool ShowingDeleted { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public Func<bool>? CanSwitch { get; set; }

    public ProfilePending Pending { get; private set; }

    public ProfileInfo? PendingProfile { get; private set; }

    public string ActiveId => _store.ActiveId;

    public ProfileInfo? Selected => _list.Selected;

    public string Summary => ShowingDeleted
        ? FormattableString.Invariant($"{_rows.Count} deleted profile(s)")
        : FormattableString.Invariant($"{_rows.Count} profile(s), active: {ActiveName}");

    public string DetailText
    {
        get
        {
            var text = new StringBuilder();
            if (Selected is { } p)
            {
                text.AppendLine(p.Name);
                text.AppendLine(ShowingDeleted ? "State: deleted (can be restored)"
                    : p.Id == ActiveId ? "State: active" : "State: not active");
                if (p.IsDefault) text.AppendLine("This is the default profile and cannot be deleted.");
            }
            else
            {
                text.AppendLine(ShowingDeleted ? "No deleted profiles." : "No profiles.");
            }
            if (Status.Length > 0) text.AppendLine().Append(Status);
            return text.ToString().TrimEnd();
        }
    }

    public void Reload()
    {
        _rows.Clear();
        _rows.AddRange(ShowingDeleted ? _store.Deleted() : _store.List());
        _list.Refresh();
    }

    public void ToggleDeleted()
    {
        if (Pending != ProfilePending.None) return;
        ShowingDeleted = !ShowingDeleted;
        Status = string.Empty;
        Reload();
    }

    public bool RequestCreate()
    {
        if (Pending != ProfilePending.None || ShowingDeleted) return false;
        NameRequested?.Invoke(new NameRequest(NameRequestKind.Create, string.Empty));
        return true;
    }

    public bool RequestRename()
    {
        if (Pending != ProfilePending.None || ShowingDeleted || Selected is not { } p) return false;
        _renameId = p.Id;
        NameRequested?.Invoke(new NameRequest(NameRequestKind.Rename, p.Name));
        return true;
    }

    public void RequestFailed(string message)
    {
        _renameId = null;
        Say(message);
    }

    public bool Create(string name)
    {
        if (Pending != ProfilePending.None) return false;
        if (ProfileNames.Check(name) is { } error) return Refuse(error);
        var result = _store.Create(ProfileNames.Normalize(name));
        if (!result.Ok || result.Profile is not { } created) return Refuse("Create failed: " + result.Message);
        ShowingDeleted = false;
        Reload();
        _list.Select(_list.Rows.ToList().FindIndex(p => p.Id == created.Id));
        Say("Created \"" + created.Name + "\". It is not active until you choose it.");
        return true;
    }

    public bool Rename(string name)
    {
        var id = _renameId;
        _renameId = null;
        if (id is null || Pending != ProfilePending.None) return false;
        if (ProfileNames.Check(name) is { } error) return Refuse(error);
        var result = _store.Rename(id, ProfileNames.Normalize(name));
        Reload();
        _list.Select(_list.Rows.ToList().FindIndex(p => p.Id == id));
        Say(result.Ok ? "Renamed to \"" + ProfileNames.Normalize(name) + "\"." : "Rename failed: " + result.Message);
        return result.Ok;
    }

    public bool RequestDelete()
    {
        if (Pending != ProfilePending.None || Selected is not { } p) return false;
        if (ShowingDeleted) return Refuse("Deleted profiles can only be restored.");
        if (p.IsDefault) return Refuse("The default profile cannot be deleted.");
        if (p.Id == ActiveId) return Refuse("The active profile cannot be deleted. Choose another profile first.");
        return Ask(ProfilePending.Delete, p, new ConfirmRequest("Delete profile",
            "Delete \"" + p.Name + "\"? It moves to Deleted and can be restored.", "Delete"));
    }

    public bool Choose()
    {
        if (Pending != ProfilePending.None || Selected is not { } p) return false;
        if (ShowingDeleted) return Restore();
        if (p.Id == ActiveId) return Refuse("\"" + p.Name + "\" is already the active profile.");
        if (CanSwitch is { } can && !can()) return Refuse(SwitchUnavailable);
        if (!_inGame) return PerformSwitch(p);
        return Ask(ProfilePending.Switch, p, new ConfirmRequest("Switch profile",
            "Switch to \"" + p.Name + "\"? You will return to the title screen and lose unsaved progress.", "Switch"));
    }

    public bool Restore()
    {
        if (Pending != ProfilePending.None || !ShowingDeleted || Selected is not { } p) return false;
        var result = _store.Restore(p.Id);
        Reload();
        Say(result.Ok ? "Restored \"" + p.Name + "\"." : "Restore failed: " + result.Message);
        return result.Ok;
    }

    public bool ConfirmPending()
    {
        var kind = Pending;
        var profile = PendingProfile;
        Pending = ProfilePending.None;
        PendingProfile = null;
        if (profile is null) return false;
        if (kind == ProfilePending.Switch) return PerformSwitch(profile);
        if (kind != ProfilePending.Delete) return false;
        var result = _store.Delete(profile.Id);
        Reload();
        Say(result.Ok ? "Moved \"" + profile.Name + "\" to Deleted." : "Delete failed: " + result.Message);
        return result.Ok;
    }

    public void CancelPending(string message = "Cancelled.")
    {
        if (Pending == ProfilePending.None) return;
        Pending = ProfilePending.None;
        PendingProfile = null;
        Say(message);
    }

    public void SwitchFailed(string message)
    {
        if (_previousId.Length == 0)
        {
            Say(message);
            return;
        }
        var result = _store.Choose(_previousId);
        Reload();
        Say(result.Ok
            ? message + " The previous profile is still active."
            : message + " The previous profile could not be restored: " + result.Message);
    }

    private string ActiveName => _store.List().FirstOrDefault(p => p.Id == ActiveId)?.Name ?? ActiveId;

    private bool PerformSwitch(ProfileInfo profile)
    {
        _previousId = _store.ActiveId;
        var result = _store.Choose(profile.Id);
        Reload();
        if (!result.Ok) return Refuse("Could not switch profile: " + result.Message);
        Say("Switched to \"" + profile.Name + "\".");
        ProfileChosen?.Invoke(profile);
        return true;
    }

    private bool Ask(ProfilePending kind, ProfileInfo profile, ConfirmRequest request)
    {
        Pending = kind;
        PendingProfile = profile;
        Say(request.Body + " Press Accept to confirm or Back to cancel.");
        ConfirmRequested?.Invoke(request);
        return true;
    }

    private bool Handle(MenuInputAction action)
    {
        if (Pending != ProfilePending.None)
        {
            if (action == MenuInputAction.Accept) ConfirmPending();
            else if (action == MenuInputAction.Back) CancelPending();
            return true;
        }
        switch (action)
        {
            case MenuInputAction.Accept: Choose(); return true;
            case MenuInputAction.Compare: RequestCreate(); return true;
            case MenuInputAction.Favourite: RequestRename(); return true;
            case MenuInputAction.TabNext: RequestDelete(); return true;
            case MenuInputAction.TabPrev: ToggleDeleted(); return true;
            case MenuInputAction.Back:
                if (!ShowingDeleted) return false;
                ToggleDeleted();
                return true;
            default: return false;
        }
    }

    private IReadOnlyList<MenuHint> Hints()
    {
        if (Pending != ProfilePending.None)
            return [new MenuHint(MenuInputAction.Accept, "Confirm"), new MenuHint(MenuInputAction.Back, "Cancel")];
        if (ShowingDeleted)
            return
            [
                new MenuHint(MenuInputAction.Accept, "Restore"),
                new MenuHint(MenuInputAction.Back, "Show profiles"),
                new MenuHint(MenuInputAction.Search, "Search"),
                new MenuHint(MenuInputAction.TabPrev, "Show profiles"),
            ];
        return
        [
            new MenuHint(MenuInputAction.Accept, "Choose"),
            new MenuHint(MenuInputAction.Back, "Back"),
            new MenuHint(MenuInputAction.Search, "Search"),
            new MenuHint(MenuInputAction.Compare, "New profile"),
            new MenuHint(MenuInputAction.Favourite, "Rename"),
            new MenuHint(MenuInputAction.TabNext, "Delete"),
            new MenuHint(MenuInputAction.TabPrev, "Show deleted"),
        ];
    }

    private string RowText(ProfileInfo profile)
    {
        if (ShowingDeleted) return profile.Name + " (deleted)";
        var active = profile.Id == ActiveId;
        return (active ? "* " : "  ") + profile.Name + (profile.IsDefault ? " (default)" : string.Empty) + (active ? " (active)" : string.Empty);
    }

    private bool Refuse(string status)
    {
        Say(status);
        return false;
    }

    private void Say(string status)
    {
        Status = status;
        _list.Refresh();
    }
}
