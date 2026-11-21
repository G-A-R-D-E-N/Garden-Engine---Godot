using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.PipBoy;

// Requires: main thread only; one binding per open Pip-Boy, and it routes Equip, Unequip, Use and Drop.
public sealed class InventoryEquipBinding
{
    private readonly PlayerItems _items;
    private readonly IItemRecords _records;
    private readonly IRecordReader _reader;
    private readonly PlayerEquipment _equipment;
    private readonly WeaponController _weapons;
    private readonly InventoryActions _actions;
    private readonly Func<string, IReadOnlyList<ConsumableEffect>?>? _effects;
    private readonly Func<double?>? _capacity;
    private InventoryModel? _model;

    public InventoryEquipBinding(PlayerItems items, IItemRecords records, IRecordReader reader, PlayerEquipment equipment,
                                 WeaponController weapons, IInventoryHost host,
                                 Func<string, IReadOnlyList<ConsumableEffect>?>? effects = null, Func<double?>? capacity = null)
    {
        _effects = effects;
        _capacity = capacity;
        _items = items;
        _records = records;
        _reader = reader;
        _equipment = equipment;
        _weapons = weapons;
        _actions = new InventoryActions(items, records, reader, equipment, host);
    }

    public InventoryModel NewModel() => _model = new InventoryModel(Build(), store: PipBoyMemory.Current.Favourites) { Capacity = _capacity?.Invoke() ?? 0 };

    public InventoryActionResult? Handle(InventoryEvent command)
    {
        if (command.Kind is not (InventoryEventKind.Equip or InventoryEventKind.Unequip or InventoryEventKind.Use or InventoryEventKind.Drop)) return null;
        SyncHeld();
        var result = _actions.Apply(command);
        _model?.Sync(Build());
        if (_model is not null) _model.Capacity = _capacity?.Invoke() ?? 0;
        return result;
    }

    private IReadOnlyList<InventoryItem> Build()
    {
        SyncHeld();
        return PlayerInventoryItems.Build(_items.Stacks, _records, _equipment, _reader, id => _effects?.Invoke(id) is not null, _effects, _items);
    }

    // Constraint: the controller owns the held weapon (the draw key and SyncHeld change it behind the equipment's back).
    private void SyncHeld()
    {
        _weapons.SyncHeld();
        if (_weapons.WeaponKey is { } key) _equipment.SetWeapon(key, _weapons.Item);
        else _equipment.ClearWeapon();
        _equipment.Reconcile(_items);
    }
}
