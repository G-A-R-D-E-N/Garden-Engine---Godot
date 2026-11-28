using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Settings;

public sealed class DisplayTextListView<TItem> : IListMenuView
{
    private readonly ListMenuModel<TItem> _inner;
    private readonly Func<TItem, string> _display;

    public DisplayTextListView(ListMenuModel<TItem> inner, Func<TItem, string> display)
    {
        _inner = inner;
        _display = display;
    }

    public event Action? Changed
    {
        add => _inner.Changed += value;
        remove => _inner.Changed -= value;
    }

    public int RowCount => _inner.RowCount;

    public int SelectedIndex => _inner.SelectedIndex;

    public int ScrollTop => _inner.ScrollTop;

    public int ViewRows => _inner.ViewRows;

    public string SearchText => _inner.SearchText;

    public bool IsSearching => _inner.IsSearching;

    public IReadOnlyList<string> Tabs => _inner.Tabs;

    public int TabIndex => _inner.TabIndex;

    public IReadOnlyList<string> SortNames => _inner.SortNames;

    public int SortIndex => _inner.SortIndex;

    public bool SortDescending => _inner.SortDescending;

    public bool IsComparing => _inner.IsComparing;

    public string CompareHeading => _inner.CompareHeading;

    public IReadOnlyList<StatComparison> CompareRows => _inner.CompareRows;

    public Func<string>? SummaryText { get; set; }

    public string Summary => SummaryText?.Invoke() ?? _inner.Summary;

    public IReadOnlyList<MenuHint> Hints => _inner.Hints;

    public void SetViewRows(int rows) => _inner.SetViewRows(rows);

    public string RowText(int index) => index >= 0 && index < _inner.RowCount ? _display(_inner.Rows[index]) : string.Empty;

    public string RowDetail(int index) => _inner.RowDetail(index);

    public bool RowIsFavourite(int index) => _inner.RowIsFavourite(index);

    public void SetSearch(string? text) => _inner.SetSearch(text);

    public void BeginSearch() => _inner.BeginSearch();

    public void EndSearch(bool clear) => _inner.EndSearch(clear);

    public void SelectTab(int index) => _inner.SelectTab(index);

    public void SortBy(int index) => _inner.SortBy(index);

    public bool Select(int index) => _inner.Select(index);

    public bool Activate(int index) => _inner.Activate(index);

    public bool ToggleFavouriteAt(int index) => _inner.ToggleFavouriteAt(index);

    public bool Handle(MenuInputAction action) => _inner.Handle(action);
}
