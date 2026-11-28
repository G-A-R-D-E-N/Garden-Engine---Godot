using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public sealed class SaveSlotListView : IListMenuView
{
    public const string AllTab = "All";
    public const string FavouritesTab = "Favourites";

    private static readonly SaveSort[] Sorts = { SaveSort.Date, SaveSort.Name, SaveSort.Level };
    private static readonly string[] SortLabels = { "Date", "Name", "Level" };
    private static readonly IReadOnlyList<StatComparison> NoRows = Array.Empty<StatComparison>();

    private readonly SaveSlotListModel _model;
    private string? _character;
    private bool _favouritesOnly;
    private string _search = string.Empty;
    private bool _searching;
    private int _scrollTop;
    private int _viewRows = 10;

    public SaveSlotListView(SaveSlotListModel model)
    {
        _model = model;
        _model.PageSize = _viewRows - 1;
    }

    public event Action? Changed;

    public event Action<SaveSlotInfo>? Accepted;

    public event Action<SaveSlotInfo>? DeleteRequested;

    public SaveSlotInfo? Selected => _model.Current;

    public int RowCount => _model.Visible.Count;

    public int SelectedIndex => _model.CursorIndex;

    public int ScrollTop => _scrollTop;

    public int ViewRows => _viewRows;

    public string SearchText => _search;

    public bool IsSearching => _searching;

    public IReadOnlyList<string> Tabs => new[] { AllTab }.Concat(_model.Characters).Append(FavouritesTab).ToList();

    public int TabIndex
    {
        get
        {
            var tabs = Tabs;
            if (_favouritesOnly) return tabs.Count - 1;
            var index = _character is null ? 0 : tabs.ToList().IndexOf(_character);
            return Math.Max(index, 0);
        }
    }

    public IReadOnlyList<string> SortNames => SortLabels;

    public int SortIndex => Array.IndexOf(Sorts, _model.Sort);

    public bool SortDescending => _model.Descending;

    public bool IsComparing => false;

    public string CompareHeading => string.Empty;

    public IReadOnlyList<StatComparison> CompareRows => NoRows;

    public string Summary
    {
        get
        {
            var text = FormattableString.Invariant($"{RowCount} of {_model.TotalCount} saves, sorted by {SortLabels[SortIndex]} ({(_model.Descending ? "descending" : "ascending")})");
            return _search.Length > 0 ? text + ", search \"" + _search + "\"" : text;
        }
    }

    public IReadOnlyList<MenuHint> Hints
    {
        get
        {
            if (_model.IsConfirmingDelete) return Array.Empty<MenuHint>();
            if (_searching)
                return [new MenuHint(MenuInputAction.Accept, "Done"), new MenuHint(MenuInputAction.Back, "Clear search")];
            var tabs = Tabs;
            var index = TabIndex;
            var hints = new List<MenuHint>();
            var has = _model.Current is not null;
            if (has) hints.Add(new MenuHint(MenuInputAction.Accept, "Load"));
            hints.Add(new MenuHint(MenuInputAction.Back, _search.Length > 0 ? "Clear search" : "Back"));
            hints.Add(new MenuHint(MenuInputAction.Search, "Search"));
            hints.Add(new MenuHint(MenuInputAction.Sort, "Sort: " + SortLabels[SortIndex] + (_model.Descending ? " descending" : " ascending")));
            if (has) hints.Add(new MenuHint(MenuInputAction.Compare, "Delete"));
            if (has) hints.Add(new MenuHint(MenuInputAction.Favourite, _model.IsFavourite(_model.Current!) ? "Unfavourite" : "Favourite"));
            hints.Add(new MenuHint(MenuInputAction.TabPrev, "Previous: " + tabs[Wrap(index - 1, tabs.Count)]));
            hints.Add(new MenuHint(MenuInputAction.TabNext, "Next: " + tabs[Wrap(index + 1, tabs.Count)]));
            return hints;
        }
    }

    public void SetViewRows(int rows)
    {
        rows = Math.Max(1, rows);
        if (rows == _viewRows) return;
        _viewRows = rows;
        _model.PageSize = Math.Max(1, rows - 1);
        Notify();
    }

    public string RowText(int index) => InRange(index) ? SaveSlotText.RowText(_model.Visible[index]) : string.Empty;

    public string RowDetail(int index) => InRange(index) ? SaveSlotText.RowDetail(_model.Visible[index]) : string.Empty;

    public bool RowIsFavourite(int index) => InRange(index) && _model.IsFavourite(_model.Visible[index]);

    public void SetSearch(string? text)
    {
        var next = text ?? string.Empty;
        if (next == _search) return;
        _search = next;
        _model.SetSearch(next);
        Notify();
    }

    public void BeginSearch()
    {
        if (_searching || _model.IsConfirmingDelete) return;
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
            _model.SetSearch(string.Empty);
        }
        Notify();
    }

    public void SelectTab(int index)
    {
        var tabs = Tabs;
        if (_model.IsConfirmingDelete || index < 0 || index >= tabs.Count || index == TabIndex) return;
        _favouritesOnly = index == tabs.Count - 1;
        _character = index == 0 || _favouritesOnly ? null : tabs[index];
        _model.SetFavouritesOnly(_favouritesOnly);
        _model.SetCharacter(_character);
        Notify();
    }

    public void SortBy(int index)
    {
        if (_model.IsConfirmingDelete || index < 0 || index >= Sorts.Length) return;
        if (index == SortIndex) _model.ToggleDirection();
        else _model.SetSort(Sorts[index]);
        Notify();
    }

    public bool Select(int index)
    {
        if (_model.IsConfirmingDelete || index < 0 || index >= RowCount) return false;
        if (index != _model.CursorIndex)
        {
            _model.SelectVisible(index);
            Notify();
        }
        return true;
    }

    public bool Activate(int index)
    {
        if (!Select(index)) return false;
        EndSearch(false);
        return AcceptCurrent();
    }

    public bool ToggleFavouriteAt(int index) => Select(index) && ToggleFavourite();

    public bool ConfirmDelete()
    {
        var deleted = _model.ConfirmDelete();
        Reload();
        return deleted;
    }

    public void CancelDelete()
    {
        _model.CancelDelete();
        Notify();
    }

    public void Reload()
    {
        _model.Reload();
        var tabs = Tabs;
        if (_character is not null && !tabs.Contains(_character))
        {
            _character = null;
            _model.SetCharacter(null);
        }
        Notify();
    }

    public bool Handle(MenuInputAction action)
    {
        if (_model.IsConfirmingDelete) return true;
        if (_searching) return HandleWhileSearching(action);
        switch (action)
        {
            case MenuInputAction.Up: Move(CursorMove.Up); return true;
            case MenuInputAction.Down: Move(CursorMove.Down); return true;
            case MenuInputAction.PageUp: Move(CursorMove.PageUp); return true;
            case MenuInputAction.PageDown: Move(CursorMove.PageDown); return true;
            case MenuInputAction.Accept: return AcceptCurrent();
            case MenuInputAction.Back:
                if (_search.Length == 0) return false;
                SetSearch(string.Empty);
                return true;
            case MenuInputAction.Search: BeginSearch(); return true;
            case MenuInputAction.Sort: SetNextSort(); return true;
            case MenuInputAction.Compare: return RequestDelete();
            case MenuInputAction.Favourite: return ToggleFavourite();
            case MenuInputAction.TabPrev: SelectTab(Wrap(TabIndex - 1, Tabs.Count)); return true;
            case MenuInputAction.TabNext: SelectTab(Wrap(TabIndex + 1, Tabs.Count)); return true;
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

    private bool AcceptCurrent()
    {
        if (_model.Current is not { } slot) return false;
        Accepted?.Invoke(slot);
        return true;
    }

    private bool RequestDelete()
    {
        if (!_model.RequestDelete()) return false;
        Notify();
        DeleteRequested?.Invoke(_model.PendingDelete!);
        return true;
    }

    private bool ToggleFavourite()
    {
        if (_model.Current is null) return false;
        _model.ToggleFavouriteCurrent();
        Notify();
        return true;
    }

    private void SetNextSort()
    {
        _model.SetSort(Sorts[(SortIndex + 1) % Sorts.Length]);
        Notify();
    }

    private void Move(CursorMove move)
    {
        _model.Move(move);
        Notify();
    }

    private bool InRange(int index) => index >= 0 && index < _model.Visible.Count;

    private static int Wrap(int index, int count) => ((index % count) + count) % count;

    private void Notify()
    {
        var cursor = _model.CursorIndex;
        if (cursor >= 0)
        {
            if (cursor < _scrollTop) _scrollTop = cursor;
            else if (cursor >= _scrollTop + _viewRows) _scrollTop = cursor - _viewRows + 1;
        }
        _scrollTop = Math.Clamp(_scrollTop, 0, Math.Max(0, _model.Visible.Count - _viewRows));
        Changed?.Invoke();
    }
}
