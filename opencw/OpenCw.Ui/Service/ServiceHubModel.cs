using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Service;

public sealed class ServiceHubModel
{
    private readonly Func<bool> _setupNeeded;
    private readonly List<ServiceEntry> _entries = new();
    private readonly ListMenuModel<ServiceEntry> _list;

    public ServiceHubModel(Func<bool> setupNeeded)
    {
        _setupNeeded = setupNeeded;
        _list = new ListMenuModel<ServiceEntry>(_entries, e => e.Kind.ToString(), e => e.Title);
        View = new ServiceListView<ServiceEntry>(_list, e => e.Title) { Extra = Handle, HintsOf = Hints };
        Refresh();
    }

    public event Action<ServiceKind>? OpenRequested;

    public ServiceListView<ServiceEntry> View { get; }

    public IReadOnlyList<ServiceEntry> Entries => _entries;

    public ServiceEntry? Selected => _list.Selected;

    public string DetailText => Selected is { } entry ? entry.Title + "\n\n" + entry.Description : string.Empty;

    public void Refresh()
    {
        _entries.Clear();
        _entries.AddRange(ServiceCatalog.Entries(_setupNeeded()));
        _list.Refresh();
    }

    private bool Handle(MenuInputAction action)
    {
        if (action != MenuInputAction.Accept || Selected is not { } entry)
            return false;
        OpenRequested?.Invoke(entry.Kind);
        return true;
    }

    private static IReadOnlyList<MenuHint> Hints() =>
    [
        new MenuHint(MenuInputAction.Accept, "Open"),
        new MenuHint(MenuInputAction.Back, "Back"),
    ];
}
