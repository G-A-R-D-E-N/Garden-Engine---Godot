using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record InventoryAction(string Label, Func<InventoryEvent?> Run, string? ItemId = null);

public sealed class InventoryListView : PipBoyListView<InventoryItem>
{
    private static readonly string[] Sorts = ["Name", "Weight", "Value", "Value per weight", "Recent"];

    private readonly InventoryModel _inventory;

    public InventoryListView(InventoryModel inventory)
        : base(inventory.View)
    {
        _inventory = inventory;
    }

    public event Action<InventoryEvent>? Command;

    public event Action<IReadOnlyList<InventoryAction>>? ActionsRequested;

    public InventoryModel Inventory => _inventory;

    public override IReadOnlyList<string> Tabs => ["All", .. _inventory.Categories, "Favourites"];

    public override int TabIndex
    {
        get
        {
            if (_inventory.FavouritesOnly) return _inventory.Categories.Count + 1;
            var index = _inventory.Category is null ? -1 : _inventory.Categories.ToList().IndexOf(_inventory.Category);
            return index + 1;
        }
    }

    public override IReadOnlyList<string> SortNames => Sorts;

    public override int SortIndex => (int)_inventory.Sort;

    public override bool SortDescending => _inventory.SortDescending;

    public override string Summary
    {
        get
        {
            var burden = _inventory.Burden;
            var text = FormattableString.Invariant($"{RowCount} shown, carrying {burden.Carried:0.##}");
            if (burden.Capacity > 0) text += FormattableString.Invariant($" of {burden.Capacity:0.##}");
            return burden.IsOverEncumbered ? text + " (overburdened)" : text;
        }
    }

    public QuantitySelector? Quantity => _inventory.Quantity;

    public string? Message => _inventory.Message;

    public override IReadOnlyList<MenuHint> Hints =>
        Quantity is null
            ? base.Hints
            : new[] { new MenuHint(MenuInputAction.Accept, "Accept"), new MenuHint(MenuInputAction.Back, "Cancel") };

    protected override bool Locked => Quantity is not null;

    protected override bool HasCompare => true;

    protected override string AcceptLabel => "Actions";

    protected override string? FavouriteLabel =>
        Selected is { } item && _inventory.SlotOf(item.Id) > 0 ? "Remove favourite" : "Favourite";

    protected override PipBoyCompare? Compare =>
        _inventory.EquippedCounterpart() is { } worn && _inventory.CompareWithEquipped() is { } view && Selected is { } item
            ? new PipBoyCompare(worn.Name + " (equipped)  vs  " + item.Name, view.Rows)
            : null;

    public IReadOnlyList<InventoryAction> CurrentActions()
    {
        var actions = new List<InventoryAction>();
        if (Selected is not { } item) return actions;
        if (item.Equippable)
            actions.Add(item.Equipped ? new InventoryAction("Unequip", _inventory.Unequip, item.Id) : new InventoryAction("Equip", _inventory.Equip, item.Id));
        if (item is { Equippable: true, Copies: > 1 }) actions.Add(new InventoryAction("Next copy", _inventory.EquipNextCopy, item.Id));
        if (item.Usable) actions.Add(new InventoryAction("Use", _inventory.Use, item.Id));
        var spare = InventoryModel.Spare(item);
        if (spare > 0)
        {
            actions.Add(new InventoryAction("Drop one", () => _inventory.Drop(1), item.Id));
            if (spare > InventoryModel.SingleDropLimit)
                actions.Add(new InventoryAction("Drop...", _inventory.BeginDrop, item.Id));
            if (spare > 1)
                actions.Add(new InventoryAction(string.Create(CultureInfo.InvariantCulture, $"Drop all ({spare})"), () => _inventory.Drop(spare), item.Id));
        }
        actions.Add(new InventoryAction(_inventory.SlotOf(item.Id) > 0 ? "Remove favourite" : "Add favourite", _inventory.ToggleFavouriteCurrent, item.Id));
        return actions;
    }

    public override bool Handle(MenuInputAction action) =>
        Quantity is null ? base.Handle(action) : HandleQuantity(action);

    public bool BeginDrop()
    {
        var result = _inventory.BeginDrop();
        Emit(result);
        return result is not null || Quantity is not null;
    }

    public bool StepQuantity(int direction, QuantityStep step)
    {
        var changed = _inventory.StepQuantity(direction, step);
        if (changed) Notify();
        return changed;
    }

    public bool ConfirmDrop()
    {
        var result = _inventory.ConfirmDrop();
        Emit(result);
        return result is not null;
    }

    public bool CancelDrop()
    {
        var closed = _inventory.CancelDrop();
        if (closed) Notify();
        return closed;
    }

    // Invariant: an action created for one item never runs while another item is selected.
    public bool Run(InventoryAction action)
    {
        if (action.ItemId is { } id && Selected?.Id != id)
        {
            Emit(null);
            return false;
        }
        var result = action.Run();
        Emit(result);
        return result is not null;
    }

    public bool QuickEquip(int slot)
    {
        var result = _inventory.QuickEquip(slot);
        Emit(result);
        return result is not null;
    }

    public bool AssignSlot(int slot)
    {
        var result = _inventory.AssignFavourite(slot);
        Emit(result);
        return result is not null;
    }

    protected override string TextOf(InventoryItem item) =>
        item.Name + (item.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $" ({item.Count})") : string.Empty)
        + (item.Equipped ? " (equipped)" : string.Empty);

    protected override string DetailOf(InventoryItem item) => _inventory.Sort switch
    {
        InventorySort.Value => NumberText.Format(item.Value),
        InventorySort.ValuePerWeight => double.IsInfinity(item.ValuePerWeight) ? "-" : NumberText.Format(item.ValuePerWeight),
        _ => NumberText.Format(item.Weight) + " wt",
    };

    protected override bool IsFavourite(InventoryItem item) => _inventory.SlotOf(item.Id) > 0;

    protected override bool ToggleFavourite()
    {
        var result = _inventory.ToggleFavouriteCurrent();
        Emit(result);
        return result is not null;
    }

    protected override bool OnAccept(InventoryItem item)
    {
        var actions = CurrentActions();
        if (actions.Count == 0) return false;
        ActionsRequested?.Invoke(actions);
        return true;
    }

    protected override void ApplyTab(int index)
    {
        var categories = _inventory.Categories;
        if (index > categories.Count)
        {
            _inventory.SetCategory(null);
            _inventory.SetFavouritesOnly(true);
            return;
        }
        _inventory.SetFavouritesOnly(false);
        _inventory.SetCategory(index == 0 ? null : categories[index - 1]);
    }

    protected override void ApplySort(int index, bool descending) =>
        _inventory.SetSort((InventorySort)index, descending);

    private bool HandleQuantity(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Left: StepQuantity(-1, QuantityStep.Fine); break;
            case MenuInputAction.Right: StepQuantity(1, QuantityStep.Fine); break;
            case MenuInputAction.Down: StepQuantity(-1, QuantityStep.Medium); break;
            case MenuInputAction.Up: StepQuantity(1, QuantityStep.Medium); break;
            case MenuInputAction.TabPrev: StepQuantity(-1, QuantityStep.Medium); break;
            case MenuInputAction.TabNext: StepQuantity(1, QuantityStep.Medium); break;
            case MenuInputAction.PageDown: StepQuantity(-1, QuantityStep.Coarse); break;
            case MenuInputAction.PageUp: StepQuantity(1, QuantityStep.Coarse); break;
            case MenuInputAction.Accept: ConfirmDrop(); break;
            case MenuInputAction.Back: CancelDrop(); break;
        }
        return true;
    }

    private void Emit(InventoryEvent? result)
    {
        if (result is not null) Command?.Invoke(result);
        Refresh();
    }
}
