using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Lists;

public sealed class MenuList<TItem>
{
    private readonly IReadOnlyList<TItem> _source;
    private readonly Func<TItem, string> _keyOf;
    private readonly Func<TItem, string> _textOf;
    private readonly Func<TItem, string> _categoryOf;
    private readonly IFavouriteStore _store;
    private readonly HashSet<string> _favourites;
    private readonly Dictionary<string, Func<TItem, bool>> _filters = new();
    private readonly List<TItem> _visible = new();
    private string[] _tokens = Array.Empty<string>();
    private string? _category;
    private bool _favouritesOnly;
    private SortKey<TItem>? _sort;
    private bool _descending;
    private string? _selectedKey;
    private int _cursor = -1;
    private int _pageSize = 10;

    public MenuList(IReadOnlyList<TItem> items, Func<TItem, string> keyOf, Func<TItem, string> textOf,
                    Func<TItem, string>? categoryOf = null, IFavouriteStore? store = null)
    {
        _source = items;
        _keyOf = keyOf;
        _textOf = textOf;
        _categoryOf = categoryOf ?? (_ => string.Empty);
        _store = store ?? new InMemoryFavouriteStore();
        _favourites = new HashSet<string>(_store.Load());
        Refresh();
    }

    public IReadOnlyList<TItem> Visible => _visible;

    public int CursorIndex => _cursor;

    public TItem? Current => _cursor >= 0 ? _visible[_cursor] : default;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Max(1, value);
    }

    public bool Wrap { get; set; }

    public bool IsFavourite(TItem item) => _favourites.Contains(_keyOf(item));

    public bool ToggleFavouriteCurrent()
    {
        if (Current is not { } item) return false;
        var key = _keyOf(item);
        var added = _favourites.Add(key);
        if (!added) _favourites.Remove(key);
        _store.Save(_favourites);
        Refresh();
        return added;
    }

    public void SetSearch(string? query)
    {
        _tokens = ListSearch.Tokenize(query);
        Refresh();
    }

    public void SetCategory(string? category)
    {
        _category = category;
        Refresh();
    }

    public void SetFavouritesOnly(bool on)
    {
        _favouritesOnly = on;
        Refresh();
    }

    public void SetFilter(string name, Func<TItem, bool>? predicate)
    {
        if (predicate is null) _filters.Remove(name);
        else _filters[name] = predicate;
        Refresh();
    }

    public void SetSort(SortKey<TItem>? sort, bool descending = false)
    {
        _sort = sort;
        _descending = descending;
        Refresh();
    }

    public void Refresh()
    {
        IEnumerable<TItem> arranged = _source.Where(Accepts);
        if (_sort is not null)
        {
            var comparer = Comparer<TItem>.Create(_sort.Compare);
            arranged = _descending ? arranged.OrderByDescending(x => x, comparer) : arranged.OrderBy(x => x, comparer);
        }
        _visible.Clear();
        _visible.AddRange(arranged);
        var index = _selectedKey is null ? -1 : _visible.FindIndex(item => _keyOf(item) == _selectedKey);
        Place(index >= 0 ? index : Math.Min(Math.Max(_cursor, 0), _visible.Count - 1));
    }

    public void Move(CursorMove move)
    {
        if (_visible.Count == 0) return;
        Place(move switch
        {
            CursorMove.Up => Step(-1),
            CursorMove.Down => Step(1),
            CursorMove.PageUp => Clamp(_cursor - PageSize),
            CursorMove.PageDown => Clamp(_cursor + PageSize),
            CursorMove.Home => 0,
            CursorMove.End => _visible.Count - 1,
            CursorMove.NextCategory => NextCategoryStart(),
            _ => throw new ArgumentOutOfRangeException(nameof(move), move, null),
        });
    }

    public bool SelectVisible(int index)
    {
        if (index < 0 || index >= _visible.Count) return false;
        Place(index);
        return true;
    }

    private bool Accepts(TItem item)
    {
        if (_favouritesOnly && !_favourites.Contains(_keyOf(item))) return false;
        if (_category is not null && _categoryOf(item) != _category) return false;
        if (!ListSearch.Matches(_textOf(item), _tokens)) return false;
        return _filters.Values.All(filter => filter(item));
    }

    private int Step(int delta)
    {
        var target = _cursor + delta;
        if (target < 0) return Wrap ? _visible.Count - 1 : 0;
        if (target >= _visible.Count) return Wrap ? 0 : _visible.Count - 1;
        return target;
    }

    private int Clamp(int index) => Math.Clamp(index, 0, _visible.Count - 1);

    private int NextCategoryStart()
    {
        for (var i = _cursor + 1; i < _visible.Count; i++)
        {
            if (_categoryOf(_visible[i]) != _categoryOf(_visible[i - 1])) return i;
        }
        return Wrap ? 0 : _cursor;
    }

    private void Place(int index)
    {
        _cursor = index;
        _selectedKey = index >= 0 ? _keyOf(_visible[index]) : null;
    }
}
