using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public enum SaveSort
{
    Date,
    Name,
    Level,
}

public sealed class SaveSlotListModel
{
    private readonly ISaveSource _source;
    private readonly List<SaveSlotInfo> _slots;
    private readonly MenuList<SaveSlotInfo> _list;
    private SaveSort _sort = SaveSort.Date;
    private bool _descending = true;

    public SaveSlotListModel(ISaveSource source, IFavouriteStore? favourites = null)
    {
        _source = source;
        _slots = source.List().ToList();
        _list = new MenuList<SaveSlotInfo>(_slots, s => s.Id, s => $"{s.Name} {s.Character} {s.Location}", null, favourites);
        ApplySort();
        _list.SelectVisible(0);
    }

    public IReadOnlyList<SaveSlotInfo> Visible => _list.Visible;

    public SaveSlotInfo? Current => _list.Current;

    public int TotalCount => _slots.Count;

    public int PageSize
    {
        get => _list.PageSize;
        set => _list.PageSize = Math.Max(1, value);
    }

    public bool IsFavourite(SaveSlotInfo slot) => _list.IsFavourite(slot);

    public void ToggleFavouriteCurrent()
    {
        if (_list.Current is not null) _list.ToggleFavouriteCurrent();
    }

    public void SetFavouritesOnly(bool on) => _list.SetFavouritesOnly(on);

    public int CursorIndex => _list.CursorIndex;

    public SaveSort Sort => _sort;

    public bool Descending => _descending;

    public IReadOnlyList<string> Characters =>
        _slots.Select(s => s.Character).Distinct().OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

    public SaveSlotInfo? PendingDelete { get; private set; }

    public bool IsConfirmingDelete => PendingDelete is not null;

    public void SetSearch(string? query) => _list.SetSearch(query);

    public void SetCharacter(string? character)
    {
        Func<SaveSlotInfo, bool>? filter = character is null ? null : s => s.Character == character;
        _list.SetFilter("character", filter);
    }

    public void SetSort(SaveSort sort, bool? descending = null)
    {
        _sort = sort;
        _descending = descending ?? sort != SaveSort.Name;
        ApplySort();
    }

    public void ToggleDirection()
    {
        _descending = !_descending;
        ApplySort();
    }

    public void Move(CursorMove move)
    {
        if (!IsConfirmingDelete) _list.Move(move);
    }

    public bool SelectVisible(int index) => !IsConfirmingDelete && _list.SelectVisible(index);

    public bool RequestDelete()
    {
        if (IsConfirmingDelete || _list.Current is not { } slot) return false;
        PendingDelete = slot;
        return true;
    }

    public void CancelDelete() => PendingDelete = null;

    public bool ConfirmDelete()
    {
        if (PendingDelete is not { } slot) return false;
        PendingDelete = null;
        if (!_source.Delete(slot.Id)) return false;
        Reload();
        return true;
    }

    public void Reload()
    {
        _slots.Clear();
        _slots.AddRange(_source.List());
        _list.Refresh();
        if (PendingDelete is { } pending && _slots.All(s => s.Id != pending.Id)) PendingDelete = null;
    }

    private static int Rank(SaveKind kind) => kind switch
    {
        SaveKind.Manual => 0,
        SaveKind.Quick => 1,
        _ => 2,
    };

    private static int CompareBy(SaveSort sort, SaveSlotInfo a, SaveSlotInfo b) => sort switch
    {
        SaveSort.Name => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
        SaveSort.Level => a.Level.CompareTo(b.Level),
        _ => a.Timestamp.CompareTo(b.Timestamp),
    };

    private void ApplySort()
    {
        var sort = _sort;
        var direction = _descending ? -1 : 1;
        _list.SetSort(new SortKey<SaveSlotInfo>(sort.ToString(), (a, b) =>
        {
            if (Rank(a.Kind) != Rank(b.Kind)) return Rank(a.Kind).CompareTo(Rank(b.Kind));
            var result = direction * CompareBy(sort, a, b);
            return result != 0 ? result : string.CompareOrdinal(a.Id, b.Id);
        }));
    }
}
