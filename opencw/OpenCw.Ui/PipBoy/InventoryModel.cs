using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record ItemStat(string Name, double Value, bool HigherIsBetter = true);

public sealed record InventoryItem(string Id, string Name, string Category, double Weight, double Value, int Count = 1,
                                   bool Equipped = false, int Recent = 0, string? EquipSlot = null,
                                   bool Equippable = false, bool Usable = false, IReadOnlyList<ItemStat>? Stats = null)
{
    public string EquipKey => EquipSlot ?? Category;

    public double ValuePerWeight => Weight > 0 ? Value / Weight : Value > 0 ? double.PositiveInfinity : 0;

    public IReadOnlyList<ItemStat> StatList => Stats ?? Array.Empty<ItemStat>();
}

public enum InventorySort
{
    Name,
    Weight,
    Value,
    ValuePerWeight,
    Recent,
}

public enum InventoryEventKind
{
    Use,
    Equip,
    Unequip,
    Drop,
    AssignFavourite,
    UnassignFavourite,
}

public sealed record InventoryEvent(InventoryEventKind Kind, string ItemId, int Count = 1, int Slot = 0);

public sealed record WeightSummary(double Carried, double Capacity)
{
    public double Remaining => Capacity - Carried;

    public bool IsOverEncumbered => Capacity > 0 && Carried > Capacity;
}

public sealed class InventoryModel : IPipBoyTabSource
{
    private const string SlotPrefixSeparator = ":";
    private const string FavouritesFilter = "favourites";

    public const int SingleDropLimit = 5;
    public const string ChangedMessage = "The inventory changed. Select the item again.";

    private readonly List<InventoryItem> _items;
    private readonly MenuList<InventoryItem> _list;
    private readonly string?[] _slots;
    private readonly IFavouriteStore? _store;

    public InventoryModel(IEnumerable<InventoryItem> items, int slotCount = 8, IFavouriteStore? store = null,
                          double capacity = 0)
    {
        _items = items.ToList();
        _slots = new string?[Math.Max(slotCount, 0)];
        _store = store;
        Capacity = capacity;
        LoadSlots();
        _list = new MenuList<InventoryItem>(_items, item => item.Id, item => item.Name + " " + item.Category,
                                            item => item.Category);
        SetSort(InventorySort.Name);
    }

    public MenuList<InventoryItem> View => _list;

    public IReadOnlyList<InventoryItem> Items => _items;

    public PipBoyTab Tab => PipBoyTab.Inventory;

    public string? CurrentKey => _list.Current?.Id;

    public double Capacity { get; set; }

    public string? Category { get; private set; }

    public bool FavouritesOnly { get; private set; }

    public InventorySort Sort { get; private set; }

    public bool SortDescending { get; private set; }

    public int SlotCount => _slots.Length;

    public int Revision { get; private set; }

    public QuantitySelector? Quantity { get; private set; }

    public string? Message { get; private set; }

    public WeightSummary Burden => new(_items.Sum(item => item.Weight * item.Count), Capacity);

    public IReadOnlyList<string> Categories => _items.Select(item => item.Category).Distinct().ToArray();

    public void SetSearch(string? query) => _list.SetSearch(query);

    public void SetCategory(string? category)
    {
        Category = category;
        _list.SetCategory(category);
    }

    public void SetFavouritesOnly(bool on)
    {
        FavouritesOnly = on;
        _list.SetFilter(FavouritesFilter, on ? item => SlotOf(item.Id) > 0 : null);
    }

    public void SetSort(InventorySort sort, bool? descending = null)
    {
        Sort = sort;
        SortDescending = descending ?? sort is InventorySort.Value or InventorySort.ValuePerWeight or InventorySort.Recent;
        _list.SetSort(KeyFor(sort), SortDescending);
    }

    public bool Select(string key)
    {
        var item = _items.Find(candidate => candidate.Id == key);
        if (item is null) return false;
        if (!_list.Visible.Contains(item))
        {
            _list.SetSearch(null);
            SetCategory(null);
            SetFavouritesOnly(false);
        }
        var index = _list.Visible.ToList().FindIndex(candidate => candidate.Id == key);
        return _list.SelectVisible(index);
    }

    public IReadOnlyList<PipBoySearchHit> Search(IReadOnlyList<string> tokens) =>
        _items.Where(item => ListSearch.Matches(item.Name + " " + item.Category, tokens))
              .Select(item => new PipBoySearchHit(PipBoyTab.Inventory, item.Id, item.Name,
                                                  string.Create(CultureInfo.InvariantCulture, $"{item.Category} x{item.Count}")))
              .ToArray();

    public void Upsert(InventoryItem item)
    {
        var index = _items.FindIndex(candidate => candidate.Id == item.Id);
        if (index >= 0) _items[index] = item;
        else _items.Add(item);
        if (item.Count <= 0) RemoveSlotsOf(item.Id);
        _items.RemoveAll(candidate => candidate.Count <= 0);
        Revision++;
        if (Quantity is { } open && !_items.Exists(candidate => candidate.Id == open.ItemId)) Quantity = null;
        _list.Refresh();
    }

    public void Sync(IEnumerable<InventoryItem> items)
    {
        _items.Clear();
        _items.AddRange(items.Where(item => item.Count > 0));
        for (var i = 0; i < _slots.Length; i++)
            if (_slots[i] is { } id && !_items.Exists(item => item.Id == id)) _slots[i] = null;
        SaveSlots();
        Revision++;
        if (Quantity is { } open && !_items.Exists(item => item.Id == open.ItemId)) Quantity = null;
        _list.Refresh();
    }

    public void SetCount(string id, int count)
    {
        var item = _items.Find(candidate => candidate.Id == id);
        if (item is not null) Upsert(item with { Count = count });
    }

    public void SetEquipped(string id, bool equipped)
    {
        var item = _items.Find(candidate => candidate.Id == id);
        if (item is null) return;
        if (equipped)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Equipped && _items[i].EquipKey == item.EquipKey) _items[i] = _items[i] with { Equipped = false };
            }
        }
        Upsert(item with { Equipped = equipped });
    }

    public InventoryEvent? Use() =>
        _list.Current is { Usable: true } item ? new InventoryEvent(InventoryEventKind.Use, item.Id) : null;

    public InventoryEvent? Equip() =>
        _list.Current is { Equippable: true, Equipped: false } item ? new InventoryEvent(InventoryEventKind.Equip, item.Id) : null;

    public InventoryEvent? Unequip() =>
        _list.Current is { Equipped: true } item ? new InventoryEvent(InventoryEventKind.Unequip, item.Id) : null;

    public InventoryEvent? Drop(int count = 1)
    {
        if (_list.Current is not { Equipped: false } item || count <= 0) return null;
        return new InventoryEvent(InventoryEventKind.Drop, item.Id, Math.Min(count, item.Count));
    }

    public InventoryEvent? BeginDrop()
    {
        Message = null;
        if (Quantity is not null || _list.Current is not { Equipped: false } item) return null;
        if (item.Count <= SingleDropLimit) return Drop(1);
        Quantity = new QuantitySelector(item.Id, item.Count, Revision);
        return null;
    }

    public bool StepQuantity(int direction, QuantityStep step) => Quantity?.Step(direction, step) ?? false;

    public InventoryEvent? ConfirmDrop()
    {
        if (Quantity is not { } selector) return null;
        Quantity = null;
        if (selector.Revision != Revision || !_items.Exists(item => item.Id == selector.ItemId))
        {
            Message = ChangedMessage;
            return null;
        }
        Message = null;
        return new InventoryEvent(InventoryEventKind.Drop, selector.ItemId, selector.Value);
    }

    public bool CancelDrop()
    {
        if (Quantity is null) return false;
        Quantity = null;
        Message = null;
        return true;
    }

    public InventoryItem? ItemInSlot(int slot) =>
        slot >= 1 && slot <= _slots.Length && _slots[slot - 1] is { } id ? _items.Find(item => item.Id == id) : null;

    public int SlotOf(string id) => Array.IndexOf(_slots, id) + 1;

    public InventoryEvent? AssignFavourite(int slot)
    {
        if (_list.Current is not { } item || slot < 1 || slot > _slots.Length) return null;
        var previous = SlotOf(item.Id);
        if (previous > 0) _slots[previous - 1] = null;
        _slots[slot - 1] = item.Id;
        SlotsChanged();
        return new InventoryEvent(InventoryEventKind.AssignFavourite, item.Id, 1, slot);
    }

    public InventoryEvent? UnassignFavourite(int slot)
    {
        if (slot < 1 || slot > _slots.Length || _slots[slot - 1] is not { } id) return null;
        _slots[slot - 1] = null;
        SlotsChanged();
        return new InventoryEvent(InventoryEventKind.UnassignFavourite, id, 1, slot);
    }

    public InventoryEvent? ToggleFavouriteCurrent()
    {
        if (_list.Current is not { } item) return null;
        var slot = SlotOf(item.Id);
        if (slot > 0) return UnassignFavourite(slot);
        var free = Array.IndexOf(_slots, null);
        return free < 0 ? null : AssignFavourite(free + 1);
    }

    public InventoryEvent? QuickEquip(int slot)
    {
        if (ItemInSlot(slot) is not { } item) return null;
        if (item.Equippable) return new InventoryEvent(item.Equipped ? InventoryEventKind.Unequip : InventoryEventKind.Equip, item.Id);
        return item.Usable ? new InventoryEvent(InventoryEventKind.Use, item.Id) : null;
    }

    public InventoryItem? EquippedCounterpart()
    {
        if (_list.Current is not { Equippable: true, Equipped: false } item) return null;
        return _items.Find(candidate => candidate.Equipped && candidate.EquipKey == item.EquipKey);
    }

    public CompareView<InventoryItem>? CompareWithEquipped() =>
        EquippedCounterpart() is { } worn && _list.Current is { } item ? Compare(worn, item) : null;

    public static CompareView<InventoryItem> Compare(InventoryItem left, InventoryItem right)
    {
        var stats = new List<CompareStat<InventoryItem>>();
        foreach (var stat in left.StatList.Concat(right.StatList).DistinctBy(stat => stat.Name))
        {
            var name = stat.Name;
            stats.Add(new CompareStat<InventoryItem>(name, item => item.StatList.FirstOrDefault(s => s.Name == name)?.Value ?? 0,
                                                     stat.HigherIsBetter));
        }
        stats.Add(new CompareStat<InventoryItem>("Weight", item => item.Weight, false));
        stats.Add(new CompareStat<InventoryItem>("Value", item => item.Value, true));
        return new CompareView<InventoryItem>(left, right, stats);
    }

    private static SortKey<InventoryItem> KeyFor(InventorySort sort) => sort switch
    {
        InventorySort.Name => new SortKey<InventoryItem>("Name", (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)),
        InventorySort.Weight => SortKey<InventoryItem>.By("Weight", item => item.Weight),
        InventorySort.Value => SortKey<InventoryItem>.By("Value", item => item.Value),
        InventorySort.ValuePerWeight => SortKey<InventoryItem>.By("Value per weight", item => item.ValuePerWeight),
        InventorySort.Recent => SortKey<InventoryItem>.By("Recent", item => item.Recent),
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
    };

    private void RemoveSlotsOf(string id)
    {
        var slot = SlotOf(id);
        if (slot > 0) _slots[slot - 1] = null;
        SaveSlots();
    }

    private void SlotsChanged()
    {
        SaveSlots();
        _list.Refresh();
    }

    private void LoadSlots()
    {
        if (_store is null) return;
        foreach (var entry in _store.Load())
        {
            var split = entry.IndexOf(SlotPrefixSeparator, StringComparison.Ordinal);
            if (split <= 0 || !int.TryParse(entry.AsSpan(0, split), NumberStyles.None, CultureInfo.InvariantCulture, out var slot)) continue;
            var id = entry[(split + 1)..];
            if (slot < 1 || slot > _slots.Length || id.Length == 0 || _slots[slot - 1] is not null || Array.IndexOf(_slots, id) >= 0) continue;
            _slots[slot - 1] = id;
        }
    }

    private void SaveSlots() => _store?.Save(_slots
        .Select((id, index) => id is null ? null : string.Create(CultureInfo.InvariantCulture, $"{index + 1}{SlotPrefixSeparator}{id}"))
        .OfType<string>()
        .ToArray());
}
