using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.Menus;

public sealed class ListMenuModel<TItem> : IListMenuView
{
    public const string AllTab = "All";
    public const string FavouritesTab = "Favourites";

    private static readonly IReadOnlyList<StatComparison> NoRows = Array.Empty<StatComparison>();

    private readonly IReadOnlyList<TItem> _source;
    private readonly MenuList<TItem> _list;
    private readonly Func<TItem, string> _keyOf;
    private readonly Func<TItem, string> _textOf;
    private readonly Func<TItem, string> _categoryOf;
    private readonly Func<TItem, string>? _detailOf;
    private readonly IReadOnlyList<SortKey<TItem>> _sorts;
    private readonly IReadOnlyList<CompareStat<TItem>> _stats;
    private readonly string[] _sortNames;
    private readonly List<string> _categories = new();
    private readonly List<string> _tabs = new();
    private int _sortIndex = -1;
    private bool _descending;
    private int _tabIndex;
    private string _search = string.Empty;
    private bool _searching;
    private bool _hasPin;
    private TItem _pinned = default!;
    private int _scrollTop;
    private int _viewRows = 10;

    public ListMenuModel(
        IReadOnlyList<TItem> items,
        Func<TItem, string> keyOf,
        Func<TItem, string> textOf,
        Func<TItem, string>? categoryOf = null,
        Func<TItem, string>? detailOf = null,
        IFavouriteStore? store = null,
        IReadOnlyList<SortKey<TItem>>? sorts = null,
        IReadOnlyList<CompareStat<TItem>>? stats = null)
    {
        _source = items;
        _keyOf = keyOf;
        _textOf = textOf;
        _categoryOf = categoryOf ?? (_ => string.Empty);
        _detailOf = detailOf;
        _sorts = sorts ?? Array.Empty<SortKey<TItem>>();
        _stats = stats ?? Array.Empty<CompareStat<TItem>>();
        _sortNames = _sorts.Select(sort => sort.Name).ToArray();
        _list = new MenuList<TItem>(items, keyOf, textOf, categoryOf, store) { PageSize = _viewRows - 1 };
        RebuildTabs();
        SyncScroll();
    }

    public event Action? Changed;

    public event Action<TItem>? Accepted;

    public IReadOnlyList<TItem> Rows => _list.Visible;

    public int RowCount => _list.Visible.Count;

    public int SelectedIndex => _list.CursorIndex;

    public bool HasSelection => _list.CursorIndex >= 0;

    public TItem? Selected => _list.Current;

    public int ScrollTop => _scrollTop;

    public int ViewRows => _viewRows;

    public string SearchText => _search;

    public bool IsSearching => _searching;

    public IReadOnlyList<string> Tabs => _tabs;

    public int TabIndex => _tabIndex;

    public bool FavouritesOnly => _tabIndex == _tabs.Count - 1;

    public string? Category => _tabIndex >= 1 && _tabIndex <= _categories.Count ? _categories[_tabIndex - 1] : null;

    public IReadOnlyList<string> SortNames => _sortNames;

    public int SortIndex => _sortIndex;

    public string? SortName => _sortIndex >= 0 ? _sortNames[_sortIndex] : null;

    public bool SortDescending => _descending;

    public bool HasPin => _hasPin;

    public TItem? PinnedItem => _hasPin ? _pinned : default;

    public bool IsComparing => _hasPin && _list.CursorIndex >= 0 && _keyOf(_pinned) != _keyOf(_list.Current!);

    public string CompareHeading =>
        IsComparing ? _textOf(_pinned) + "  vs  " + _textOf(_list.Current!) : string.Empty;

    public IReadOnlyList<StatComparison> CompareRows =>
        IsComparing ? new CompareView<TItem>(_pinned, _list.Current!, _stats).Rows : NoRows;

    public string Summary
    {
        get
        {
            var text = FormattableString.Invariant($"{RowCount} of {_source.Count} shown");
            if (_sortIndex >= 0)
                text += FormattableString.Invariant($", sorted by {_sortNames[_sortIndex]} ({(_descending ? "descending" : "ascending")})");
            if (_search.Length > 0) text += ", search \"" + _search + "\"";
            return text;
        }
    }

    public IReadOnlyList<MenuHint> Hints
    {
        get
        {
            if (_searching)
                return [new MenuHint(MenuInputAction.Accept, "Done"), new MenuHint(MenuInputAction.Back, "Clear search")];
            var hints = new List<MenuHint>();
            if (HasSelection) hints.Add(new MenuHint(MenuInputAction.Accept, "Select"));
            hints.Add(new MenuHint(MenuInputAction.Back, _search.Length > 0 ? "Clear search" : _hasPin ? "Clear compare" : "Back"));
            hints.Add(new MenuHint(MenuInputAction.Search, "Search"));
            if (_sorts.Count > 0)
                hints.Add(new MenuHint(MenuInputAction.Sort, _sortIndex < 0 ? "Sort"
                    : "Sort: " + _sortNames[_sortIndex] + (_descending ? " descending" : " ascending")));
            if (_stats.Count > 0 && HasSelection)
                hints.Add(new MenuHint(MenuInputAction.Compare, !_hasPin ? "Compare"
                    : _keyOf(_pinned) == _keyOf(_list.Current!) ? "Clear compare" : "Move compare"));
            if (HasSelection)
                hints.Add(new MenuHint(MenuInputAction.Favourite, _list.IsFavourite(_list.Current!) ? "Unfavourite" : "Favourite"));
            hints.Add(new MenuHint(MenuInputAction.TabPrev, "Previous: " + _tabs[WrapTab(_tabIndex - 1)]));
            hints.Add(new MenuHint(MenuInputAction.TabNext, "Next: " + _tabs[WrapTab(_tabIndex + 1)]));
            return hints;
        }
    }

    public string RowText(int index) => InRange(index) ? _textOf(_list.Visible[index]) : string.Empty;

    public string RowDetail(int index) => _detailOf is not null && InRange(index) ? _detailOf(_list.Visible[index]) : string.Empty;

    public bool RowIsFavourite(int index) => InRange(index) && _list.IsFavourite(_list.Visible[index]);

    public void Refresh()
    {
        _list.Refresh();
        RebuildTabs();
        Notify();
    }

    public void SetViewRows(int rows)
    {
        rows = Math.Max(1, rows);
        if (rows == _viewRows) return;
        _viewRows = rows;
        _list.PageSize = Math.Max(1, rows - 1);
        Notify();
    }

    public void Move(CursorMove move)
    {
        _list.Move(move);
        Notify();
    }

    public bool Select(int index)
    {
        if (index < 0 || index >= RowCount) return false;
        if (index != _list.CursorIndex)
        {
            _list.SelectVisible(index);
            Notify();
        }
        return true;
    }

    public bool Activate(int index)
    {
        if (!Select(index)) return false;
        EndSearch(false);
        return Accept();
    }

    public void SetSearch(string? text)
    {
        var next = text ?? string.Empty;
        if (next == _search) return;
        _search = next;
        _list.SetSearch(next);
        Notify();
    }

    public void BeginSearch()
    {
        if (_searching) return;
        _searching = true;
        Notify();
    }

    public void EndSearch(bool clear)
    {
        var clears = clear && _search.Length > 0;
        if (!_searching && !clears) return;
        _searching = false;
        if (clears)
        {
            _search = string.Empty;
            _list.SetSearch(string.Empty);
        }
        Notify();
    }

    public void SortBy(int index)
    {
        if (index < 0 || index >= _sorts.Count) return;
        ApplySort(index, index == _sortIndex && !_descending);
    }

    public void CycleSort()
    {
        if (_sorts.Count == 0) return;
        if (_sortIndex < 0) ApplySort(0, false);
        else if (!_descending) ApplySort(_sortIndex, true);
        else if (_sortIndex + 1 < _sorts.Count) ApplySort(_sortIndex + 1, false);
        else ApplySort(-1, false);
    }

    public void SelectTab(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _tabIndex) return;
        _tabIndex = index;
        ApplyTab();
        Notify();
    }

    public void CycleTab(int delta) => SelectTab(WrapTab(_tabIndex + delta));

    public bool ToggleFavourite()
    {
        if (!HasSelection) return false;
        _list.ToggleFavouriteCurrent();
        Notify();
        return true;
    }

    public bool ToggleFavouriteAt(int index) => Select(index) && ToggleFavourite();

    public bool TogglePin()
    {
        if (!HasSelection) return false;
        var current = _list.Current!;
        if (_hasPin && _keyOf(_pinned) == _keyOf(current)) ClearPin();
        else
        {
            _pinned = current;
            _hasPin = true;
        }
        Notify();
        return true;
    }

    public void ClearPin()
    {
        if (!_hasPin) return;
        _hasPin = false;
        _pinned = default!;
        Notify();
    }

    public bool Handle(MenuInputAction action)
    {
        if (_searching) return HandleWhileSearching(action);
        switch (action)
        {
            case MenuInputAction.Up: Move(CursorMove.Up); return true;
            case MenuInputAction.Down: Move(CursorMove.Down); return true;
            case MenuInputAction.PageUp: Move(CursorMove.PageUp); return true;
            case MenuInputAction.PageDown: Move(CursorMove.PageDown); return true;
            case MenuInputAction.Accept: return Accept();
            case MenuInputAction.Back:
                if (_search.Length > 0) { SetSearch(string.Empty); return true; }
                if (_hasPin) { ClearPin(); return true; }
                return false;
            case MenuInputAction.Search: BeginSearch(); return true;
            case MenuInputAction.Sort: CycleSort(); return true;
            case MenuInputAction.Compare: return TogglePin();
            case MenuInputAction.Favourite: return ToggleFavourite();
            case MenuInputAction.TabPrev: CycleTab(-1); return true;
            case MenuInputAction.TabNext: CycleTab(1); return true;
            default: return false;
        }
    }

    private bool HandleWhileSearching(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Up: Move(CursorMove.Up); break;
            case MenuInputAction.Down: Move(CursorMove.Down); break;
            case MenuInputAction.PageUp: Move(CursorMove.PageUp); break;
            case MenuInputAction.PageDown: Move(CursorMove.PageDown); break;
            case MenuInputAction.Accept: EndSearch(false); break;
            case MenuInputAction.Back: EndSearch(true); break;
        }
        return true;
    }

    private bool Accept()
    {
        if (!HasSelection) return false;
        Accepted?.Invoke(_list.Current!);
        return true;
    }

    private void ApplySort(int index, bool descending)
    {
        _sortIndex = index;
        _descending = descending;
        _list.SetSort(index < 0 ? null : _sorts[index], descending);
        Notify();
    }

    private void ApplyTab()
    {
        _list.SetFavouritesOnly(FavouritesOnly);
        _list.SetCategory(Category);
    }

    private void RebuildTabs()
    {
        var wasFavourites = _tabs.Count > 0 && FavouritesOnly;
        var selectedCategory = Category;
        _categories.Clear();
        foreach (var item in _source)
        {
            var category = _categoryOf(item);
            if (category.Length > 0 && !_categories.Contains(category)) _categories.Add(category);
        }
        _tabs.Clear();
        _tabs.Add(AllTab);
        _tabs.AddRange(_categories);
        _tabs.Add(FavouritesTab);
        var kept = selectedCategory is null ? -1 : _categories.IndexOf(selectedCategory);
        _tabIndex = wasFavourites ? _tabs.Count - 1 : kept >= 0 ? kept + 1 : 0;
        ApplyTab();
    }

    private int WrapTab(int index) => ((index % _tabs.Count) + _tabs.Count) % _tabs.Count;

    private bool InRange(int index) => index >= 0 && index < _list.Visible.Count;

    private void Notify()
    {
        SyncScroll();
        Changed?.Invoke();
    }

    private void SyncScroll()
    {
        var cursor = _list.CursorIndex;
        if (cursor >= 0)
        {
            if (cursor < _scrollTop) _scrollTop = cursor;
            else if (cursor >= _scrollTop + _viewRows) _scrollTop = cursor - _viewRows + 1;
        }
        _scrollTop = Math.Clamp(_scrollTop, 0, Math.Max(0, _list.Visible.Count - _viewRows));
    }
}
