using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Service;

public sealed class ContentLibraryModel
{
    private readonly IContentLibrary _library;
    private readonly List<ContentItem> _items = new();
    private readonly ListMenuModel<ContentItem> _list;

    public ContentLibraryModel(IContentLibrary library)
    {
        _library = library;
        var sorts = new[]
        {
            new SortKey<ContentItem>("Name", ByName),
            new SortKey<ContentItem>("Author", (a, b) => Then(string.Compare(a.Author, b.Author, StringComparison.OrdinalIgnoreCase), a, b)),
            new SortKey<ContentItem>("Size", (a, b) => Then(b.AssociatedBytes.CompareTo(a.AssociatedBytes), a, b)),
            new SortKey<ContentItem>("Type", (a, b) => Then(a.Type.CompareTo(b.Type), a, b)),
        };
        _list = new ListMenuModel<ContentItem>(_items, i => i.FileName, i => i.FileName + " " + i.Author + " " + i.Description,
            detailOf: i => ServiceText.Bytes(i.AssociatedBytes), sorts: sorts);
        View = new ServiceListView<ContentItem>(_list, RowText) { Extra = Handle, HintsOf = Hints };
        Reload();
    }

    public event Action<ContentItem>? ManageRequested;

    public ServiceListView<ContentItem> View { get; }

    public IReadOnlyList<ContentItem> Items => _items;

    public ContentItem? Selected => _list.Selected;

    public string Status { get; private set; } = string.Empty;

    public string DetailText
    {
        get
        {
            var text = Selected is { } item ? Details(item) : "No content matches.";
            return Status.Length > 0 ? text + "\n\n" + Status : text;
        }
    }

    public void Reload()
    {
        Status = string.Empty;
        _items.Clear();
        _items.AddRange(_library.Load());
        _list.Refresh();
    }

    public bool Manage()
    {
        if (Selected is not { } item) return false;
        ManageRequested?.Invoke(item);
        return true;
    }

    public static string Details(ContentItem item)
    {
        var text = new StringBuilder();
        text.AppendLine(item.FileName);
        text.AppendLine("Type: " + ServiceText.TypeLabel(item.Type));
        text.AppendLine("Enabled: " + (item.Enabled ? "Yes" : "No") + " (active profile)");
        text.AppendLine("Author: " + ServiceText.OrNotSupplied(item.Author));
        text.AppendLine("Description: " + ServiceText.OrNotSupplied(item.Description));
        if (item.Error.Length > 0) text.AppendLine("Error: " + item.Error);
        text.AppendLine("Associated files: " + ServiceText.Bytes(item.AssociatedBytes) + " total (not the package size)");
        foreach (var file in item.Files)
            text.AppendLine("  " + file.Path + " (" + ServiceText.Bytes(file.Bytes) + ", " + file.Bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes)");
        return text.ToString().TrimEnd();
    }

    private bool Handle(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Accept: return Manage();
            case MenuInputAction.Compare:
                Reload();
                Status = "Reloaded.";
                _list.Refresh();
                return true;
            case MenuInputAction.TabNext:
                _list.CycleSort();
                return true;
            case MenuInputAction.TabPrev:
                _list.SortBy(Math.Max(_list.SortIndex, 0));
                return true;
            default:
                return false;
        }
    }

    private IReadOnlyList<MenuHint> Hints() =>
    [
        new MenuHint(MenuInputAction.Accept, "Manage"),
        new MenuHint(MenuInputAction.Back, "Back"),
        new MenuHint(MenuInputAction.Search, "Search"),
        new MenuHint(MenuInputAction.Sort, View.SortLabel),
        new MenuHint(MenuInputAction.Compare, "Reload"),
        new MenuHint(MenuInputAction.TabPrev, View.SortIndex < 0 ? "Sort by name" : "Flip sort direction"),
        new MenuHint(MenuInputAction.TabNext, "Next sort"),
    ];

    private static string RowText(ContentItem item)
    {
        var text = (item.Enabled ? "[x] " : "[ ] ") + item.FileName + "  " + ServiceText.TypeLabel(item.Type);
        return item.Error.Length > 0 ? text + " (error)" : text;
    }

    private static int ByName(ContentItem a, ContentItem b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);

    private static int Then(int primary, ContentItem a, ContentItem b) => primary != 0 ? primary : ByName(a, b);
}
