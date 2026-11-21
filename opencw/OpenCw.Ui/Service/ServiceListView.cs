using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Service;

public sealed class ServiceListView<TItem> : IListMenuView
{
    private static readonly IReadOnlyList<StatComparison> NoRows = Array.Empty<StatComparison>();
    private static readonly IReadOnlyList<string> NoTabs = Array.Empty<string>();

    private readonly ListMenuModel<TItem> _inner;
    private readonly Func<TItem, string> _display;

    public ServiceListView(ListMenuModel<TItem> inner, Func<TItem, string> display)
    {
        _inner = inner;
        _display = display;
    }

    public event Action? Changed
    {
        add => _inner.Changed += value;
        remove => _inner.Changed -= value;
    }

    public ListMenuModel<TItem> Inner => _inner;

    public Func<MenuInputAction, bool>? Extra { get; set; }

    public Func<IReadOnlyList<MenuHint>>? HintsOf { get; set; }

    public Func<string>? SummaryText { get; set; }

    public TItem? Selected => _inner.Selected;

    public int RowCount => _inner.RowCount;

    public int SelectedIndex => _inner.SelectedIndex;

    public int ScrollTop => _inner.ScrollTop;

    public int ViewRows => _inner.ViewRows;

    public string SearchText => _inner.SearchText;

    public bool IsSearching => _inner.IsSearching;

    public IReadOnlyList<string> Tabs => NoTabs;

    public int TabIndex => 0;

    public IReadOnlyList<string> SortNames => _inner.SortNames;

    public int SortIndex => _inner.SortIndex;

    public bool SortDescending => _inner.SortDescending;

    public bool IsComparing => false;

    public string CompareHeading => string.Empty;

    public IReadOnlyList<StatComparison> CompareRows => NoRows;

    public string Summary => SummaryText?.Invoke() ?? _inner.Summary;

    public string SortLabel => _inner.SortIndex < 0
        ? "Sort"
        : "Sort: " + _inner.SortName + (_inner.SortDescending ? " descending" : " ascending");

    public IReadOnlyList<MenuHint> Hints =>
        !_inner.IsSearching && HintsOf is { } hints ? hints() : _inner.Hints;

    public void SetViewRows(int rows) => _inner.SetViewRows(rows);

    public string RowText(int index) =>
        index >= 0 && index < _inner.RowCount ? _display(_inner.Rows[index]) : string.Empty;

    public string RowDetail(int index) => _inner.RowDetail(index);

    public bool RowIsFavourite(int index) => false;

    public void SetSearch(string? text) => _inner.SetSearch(text);

    public void BeginSearch() => _inner.BeginSearch();

    public void EndSearch(bool clear) => _inner.EndSearch(clear);

    public void SelectTab(int index)
    {
    }

    public void SortBy(int index) => _inner.SortBy(index);

    public bool Select(int index) => _inner.Select(index);

    public bool Activate(int index)
    {
        if (!_inner.Select(index)) return false;
        _inner.EndSearch(false);
        return Extra?.Invoke(MenuInputAction.Accept) ?? false;
    }

    public bool ToggleFavouriteAt(int index) => false;

    public bool Handle(MenuInputAction action)
    {
        if (_inner.IsSearching || (action == MenuInputAction.Back && _inner.SearchText.Length > 0))
            return _inner.Handle(action);
        if (Extra?.Invoke(action) == true) return true;
        return action is not (MenuInputAction.TabPrev or MenuInputAction.TabNext
            or MenuInputAction.Favourite or MenuInputAction.Compare) && _inner.Handle(action);
    }
}
