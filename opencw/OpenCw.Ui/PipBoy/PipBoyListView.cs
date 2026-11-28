using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record PipBoyCompare(string Heading, IReadOnlyList<StatComparison> Rows);

public interface IFavouriteListView : IListMenuView
{
}

public abstract class PipBoyListView<TItem> : IListMenuView
{
    private readonly MenuList<TItem> _list;
    private int _scrollTop;
    private int _viewRows = 10;
    private bool _compareOn = true;

    protected PipBoyListView(MenuList<TItem> list)
    {
        _list = list;
        _list.PageSize = _viewRows - 1;
    }

    public event Action? Changed;

    public MenuList<TItem> Source => _list;

    public TItem? Selected => _list.Current;

    public bool HasSelection => _list.CursorIndex >= 0;

    public int RowCount => _list.Visible.Count;

    public int SelectedIndex => _list.CursorIndex;

    public int ScrollTop => _scrollTop;

    public int ViewRows => _viewRows;

    public string SearchText => string.Empty;

    public bool IsSearching => false;

    public virtual IReadOnlyList<string> Tabs => ["All"];

    public virtual int TabIndex => 0;

    public virtual IReadOnlyList<string> SortNames => [];

    public virtual int SortIndex => -1;

    public virtual bool SortDescending => false;

    public bool IsComparing => _compareOn && Compare is not null;

    public string CompareHeading => IsComparing ? Compare!.Heading : string.Empty;

    public IReadOnlyList<StatComparison> CompareRows => IsComparing ? Compare!.Rows : [];

    public virtual string Summary => FormattableString.Invariant($"{RowCount} shown");

    public virtual IReadOnlyList<MenuHint> Hints
    {
        get
        {
            var hints = new List<MenuHint>();
            if (HasSelection) hints.Add(new MenuHint(MenuInputAction.Accept, AcceptLabel));
            hints.Add(new MenuHint(MenuInputAction.Back, "Back"));
            if (SortNames.Count > 0) hints.Add(new MenuHint(MenuInputAction.Sort, "Sort: " + SortNames[Math.Max(SortIndex, 0)] + (SortDescending ? " descending" : " ascending")));
            if (HasCompare && HasSelection) hints.Add(new MenuHint(MenuInputAction.Compare, _compareOn ? "Hide compare" : "Show compare"));
            if (HasSelection && FavouriteLabel is { } favourite) hints.Add(new MenuHint(MenuInputAction.Favourite, favourite));
            hints.AddRange(ExtraHints);
            return hints;
        }
    }

    protected virtual bool Locked => false;

    protected virtual PipBoyCompare? Compare => null;

    protected virtual bool HasCompare => false;

    protected virtual string AcceptLabel => "Select";

    protected virtual string? FavouriteLabel => null;

    protected virtual IEnumerable<MenuHint> ExtraHints => [];

    protected abstract string TextOf(TItem item);

    protected virtual string DetailOf(TItem item) => string.Empty;

    protected virtual bool IsFavourite(TItem item) => false;

    protected virtual bool ToggleFavourite() => false;

    protected virtual bool OnAccept(TItem item) => false;

    protected virtual void SelectionChanged()
    {
    }

    protected virtual void ApplyTab(int index)
    {
    }

    protected virtual void ApplySort(int index, bool descending)
    {
    }

    public string RowText(int index) => InRange(index) ? TextOf(_list.Visible[index]) : string.Empty;

    public string RowDetail(int index) => InRange(index) ? DetailOf(_list.Visible[index]) : string.Empty;

    public bool RowIsFavourite(int index) => InRange(index) && IsFavourite(_list.Visible[index]);

    public void Refresh()
    {
        _list.Refresh();
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
        if (Locked || index < 0 || index >= Tabs.Count || index == TabIndex) return;
        ApplyTab(index);
        _list.Refresh();
        Notify();
    }

    public void CycleTab(int delta)
    {
        if (Locked) return;
        var count = Tabs.Count;
        SelectTab(((TabIndex + delta) % count + count) % count);
    }

    public void SortBy(int index)
    {
        if (Locked || index < 0 || index >= SortNames.Count) return;
        ApplySort(index, index == SortIndex && !SortDescending);
        _list.Refresh();
        Notify();
    }

    public void CycleSort()
    {
        if (Locked || SortNames.Count == 0) return;
        if (SortIndex < 0) SortBy(0);
        else if (!SortDescending) ApplyThenNotify(SortIndex, true);
        else ApplyThenNotify((SortIndex + 1) % SortNames.Count, false);
    }

    public bool Select(int index)
    {
        if (Locked || index < 0 || index >= RowCount) return false;
        if (index != _list.CursorIndex)
        {
            _list.SelectVisible(index);
            SelectionChanged();
            Notify();
        }
        return true;
    }

    public bool Activate(int index) => Select(index) && Accept();

    public bool ToggleFavouriteAt(int index) => Select(index) && ToggleFavouriteCurrent();

    public virtual bool Handle(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Up: Move(CursorMove.Up); return true;
            case MenuInputAction.Down: Move(CursorMove.Down); return true;
            case MenuInputAction.PageUp: Move(CursorMove.PageUp); return true;
            case MenuInputAction.PageDown: Move(CursorMove.PageDown); return true;
            case MenuInputAction.Left: CycleTab(-1); return true;
            case MenuInputAction.Right: CycleTab(1); return true;
            case MenuInputAction.Accept: return Accept();
            case MenuInputAction.Sort: CycleSort(); return SortNames.Count > 0;
            case MenuInputAction.Compare: return ToggleCompare();
            case MenuInputAction.Favourite: return ToggleFavouriteCurrent();
            default: return false;
        }
    }

    protected void Notify()
    {
        SyncScroll();
        Changed?.Invoke();
    }

    private void Move(CursorMove move)
    {
        var before = _list.CursorIndex;
        _list.Move(move);
        if (_list.CursorIndex != before) SelectionChanged();
        Notify();
    }

    private bool Accept()
    {
        if (!HasSelection || !OnAccept(_list.Current!)) return false;
        _list.Refresh();
        Notify();
        return true;
    }

    private bool ToggleFavouriteCurrent()
    {
        if (!HasSelection || !ToggleFavourite()) return false;
        _list.Refresh();
        Notify();
        return true;
    }

    private bool ToggleCompare()
    {
        if (!HasCompare) return false;
        _compareOn = !_compareOn;
        Notify();
        return true;
    }

    private void ApplyThenNotify(int index, bool descending)
    {
        ApplySort(index, descending);
        _list.Refresh();
        Notify();
    }

    private bool InRange(int index) => index >= 0 && index < _list.Visible.Count;

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
