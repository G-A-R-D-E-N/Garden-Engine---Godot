using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.Menus;

public sealed class PlainListModel<TRow> : IListMenuView
{
    private static readonly IReadOnlyList<string> NoNames = Array.Empty<string>();
    private static readonly IReadOnlyList<StatComparison> NoComparison = Array.Empty<StatComparison>();
    private static readonly IReadOnlyList<MenuHint> NoHints = Array.Empty<MenuHint>();

    private readonly Func<TRow, string> _keyOf;
    private readonly Func<TRow, string> _textOf;
    private readonly Func<TRow, string>? _detailOf;
    private readonly List<TRow> _rows = new();
    private string _summary = string.Empty;
    private int _cursor = -1;
    private int _scrollTop;
    private int _viewRows = 10;

    public PlainListModel(Func<TRow, string> keyOf, Func<TRow, string> textOf, Func<TRow, string>? detailOf = null)
    {
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(textOf);
        _keyOf = keyOf;
        _textOf = textOf;
        _detailOf = detailOf;
    }

    public event Action? Changed;

    public event Action<TRow>? Accepted;

    public IReadOnlyList<TRow> Rows => _rows;

    public int RowCount => _rows.Count;

    public int SelectedIndex => _cursor;

    public bool HasSelection => _cursor >= 0;

    public TRow? Selected => _cursor >= 0 ? _rows[_cursor] : default;

    public int ScrollTop => _scrollTop;

    public int ViewRows => _viewRows;

    public string SearchText => string.Empty;

    public bool IsSearching => false;

    public IReadOnlyList<string> Tabs => NoNames;

    public int TabIndex => 0;

    public IReadOnlyList<string> SortNames => NoNames;

    public int SortIndex => -1;

    public bool SortDescending => false;

    public bool IsComparing => false;

    public string CompareHeading => string.Empty;

    public IReadOnlyList<StatComparison> CompareRows => NoComparison;

    public IReadOnlyList<MenuHint> Hints => NoHints;

    public string Summary
    {
        get => _summary;
        set
        {
            var next = value ?? string.Empty;
            if (next == _summary) return;
            _summary = next;
            Notify();
        }
    }

    public void SetRows(IEnumerable<TRow> rows, string? selectKey = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var key = selectKey ?? (_cursor >= 0 ? _keyOf(_rows[_cursor]) : null);
        var old = _cursor;
        _rows.Clear();
        _rows.AddRange(rows);
        var index = key is null ? -1 : _rows.FindIndex(row => _keyOf(row) == key);
        if (index < 0) index = _rows.Count == 0 ? -1 : Math.Clamp(old, 0, _rows.Count - 1);
        _cursor = index;
        Notify();
    }

    public void ReplaceRows(IEnumerable<TRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows.Clear();
        _rows.AddRange(rows);
        _cursor = _rows.Count == 0 ? -1 : 0;
        _scrollTop = 0;
        Notify();
    }

    public void ShowRows(IEnumerable<TRow> rows, string? selectKey)
    {
        if (selectKey is null) ReplaceRows(rows);
        else SetRows(rows, selectKey);
    }

    public bool SelectKey(string key)
    {
        var index = _rows.FindIndex(row => _keyOf(row) == key);
        return index >= 0 && Select(index);
    }

    public void SetViewRows(int rows)
    {
        rows = Math.Max(1, rows);
        if (rows == _viewRows) return;
        _viewRows = rows;
        Notify();
    }

    public string RowText(int index) => InRange(index) ? _textOf(_rows[index]) : string.Empty;

    public string RowDetail(int index) => _detailOf is not null && InRange(index) ? _detailOf(_rows[index]) : string.Empty;

    public bool RowIsFavourite(int index) => false;

    public void SetSearch(string? text)
    {
    }

    public void BeginSearch()
    {
    }

    public void EndSearch(bool clear)
    {
    }

    public void SelectTab(int index)
    {
    }

    public void SortBy(int index)
    {
    }

    public bool ToggleFavouriteAt(int index) => false;

    public bool Select(int index)
    {
        if (!InRange(index)) return false;
        if (index != _cursor)
        {
            _cursor = index;
            Notify();
        }
        return true;
    }

    public bool Activate(int index) => Select(index) && Accept();

    public bool Handle(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Up: return Step(-1);
            case MenuInputAction.Down: return Step(1);
            case MenuInputAction.PageUp: return Step(-Math.Max(1, _viewRows - 1));
            case MenuInputAction.PageDown: return Step(Math.Max(1, _viewRows - 1));
            case MenuInputAction.Accept: return Accept();
            default: return false;
        }
    }

    private bool Step(int delta)
    {
        if (_rows.Count == 0) return true;
        Select(Math.Clamp(_cursor + delta, 0, _rows.Count - 1));
        return true;
    }

    private bool Accept()
    {
        if (_cursor < 0) return false;
        Accepted?.Invoke(_rows[_cursor]);
        return true;
    }

    private bool InRange(int index) => index >= 0 && index < _rows.Count;

    private void Notify()
    {
        SyncScroll();
        Changed?.Invoke();
    }

    private void SyncScroll()
    {
        if (_cursor >= 0)
        {
            if (_cursor < _scrollTop) _scrollTop = _cursor;
            else if (_cursor >= _scrollTop + _viewRows) _scrollTop = _cursor - _viewRows + 1;
        }
        _scrollTop = Math.Clamp(_scrollTop, 0, Math.Max(0, _rows.Count - _viewRows));
    }
}
