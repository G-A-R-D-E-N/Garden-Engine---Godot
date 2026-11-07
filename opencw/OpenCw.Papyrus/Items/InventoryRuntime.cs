using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Items;

/// <summary>Where an item lives.</summary>
public enum ItemStorage
{
    /// <summary>Held by an actor.</summary>
    Carried,

    /// <summary>Worn by an actor.</summary>
    Equipped,

    /// <summary>Inside a container, whose semantics are not proven.</summary>
    Container,
}

public enum ItemOperation
{
    Add,
    Remove,
    Equip,
}

public enum ItemState
{
    Applied,

    /// <summary>The operation's key is not proven, so nothing changed.</summary>
    Refused,

    /// <summary>The storage semantics are not proven, so nothing changed.</summary>
    UnsupportedStorage,
}

public sealed record ItemOutcome(PapyrusOwnerId Owner, ItemOperation Operation, ItemStorage Storage, ItemState State, int Count, string Reason);

/// <summary>
/// Item owners, inventory/equipment operations and their events.
/// </summary>
/// <remarks>
/// A world item is owned by its placed reference and an inventory item by a dynamic owner that carries
/// the actor and the stack, so two stacks of one item in one actor are distinct owners. Each of Add,
/// Remove and Equip has its own evidence key, and the inventory state — the count and the equipped flag
/// — lives here, which makes this runtime authoritative for it. Storage inside a container is its own
/// unproven semantics and is refused and counted rather than treated like a carried stack. Inventory
/// events are delivered only to an actor that registered for them, and a registration's filter decides
/// whether the event is delivered or filtered out.
/// </remarks>
public sealed class InventoryRuntime
{
    public const string AddKey = "item.native.add";
    public const string RemoveKey = "item.native.remove";
    public const string EquipKey = "item.native.equip";
    public const string StorageKey = "item.storage.container";
    public const string WorldOwnerKey = "item.owner.world";
    public const string InventoryOwnerKey = "item.owner.inventory";

    private readonly PapyrusEventBridge _bridge;
    private readonly ScriptRegistrationRegistry _registrations;
    private readonly EvidenceGate _gate;
    private readonly Dictionary<(PapyrusOwnerId Actor, string Item), int> _counts = new();
    private readonly Dictionary<PapyrusOwnerId, string> _equipped = new();
    private readonly Dictionary<(PapyrusOwnerId Actor, string Name), string> _filters = new();

    public InventoryRuntime(
        PapyrusEventBridge bridge,
        ScriptRegistrationRegistry registrations,
        EvidenceGate gate)
    {
        _bridge = bridge;
        _registrations = registrations;
        _gate = gate;
    }

    public int Applied { get; private set; }

    public int Refused { get; private set; }

    public int UnsupportedStorage { get; private set; }

    /// <summary>The owner of a world item: its placed reference.</summary>
    public static PapyrusOwnerId WorldItem(string baseForm, string reference) =>
        PapyrusOwnerId.ForPlacedReference(baseForm, reference);

    /// <summary>The owner of one stack of an item held by an actor.</summary>
    public static PapyrusOwnerId InventoryItem(PapyrusOwnerId actor, string itemForm, int stack) =>
        PapyrusOwnerId.ForDynamicReference(itemForm, $"{actor.Key}#{stack.ToString(CultureInfo.InvariantCulture)}");

    public int CountOf(PapyrusOwnerId actor, string? itemForm) => itemForm is null
        ? _counts.Where(entry => entry.Key.Actor == actor).Sum(entry => entry.Value)
        : _counts.TryGetValue((actor, itemForm), out var count) ? count : 0;

    public string? EquippedOf(PapyrusOwnerId actor) => _equipped.TryGetValue(actor, out var item) ? item : null;

    /// <summary>Sets the filter a registration carries for one actor and event.</summary>
    public void SetFilter(PapyrusOwnerId actor, string eventName, string filter) =>
        _filters[(actor, eventName)] = filter;

    public ItemOutcome Apply(PapyrusOwnerId actor, string itemForm, ItemOperation operation, ItemStorage storage)
    {
        if (storage == ItemStorage.Container)
        {
            UnsupportedStorage++;
            return new ItemOutcome(actor, operation, storage, ItemState.UnsupportedStorage, CountOf(actor, itemForm),
                "container storage semantics are not proven, so nothing changed");
        }

        var key = operation switch
        {
            ItemOperation.Add => AddKey,
            ItemOperation.Remove => RemoveKey,
            _ => EquipKey,
        };
        if (!_gate.Gate(key).Allowed)
        {
            Refused++;
            return new ItemOutcome(actor, operation, storage, ItemState.Refused, CountOf(actor, itemForm),
                $"the {operation} operation is not proven, so nothing changed");
        }

        var slot = (actor, itemForm);
        var current = CountOf(actor, itemForm);
        switch (operation)
        {
            case ItemOperation.Add:
                _counts[slot] = current + 1;
                break;
            case ItemOperation.Remove:
                if (current == 0)
                {
                    Refused++;
                    return new ItemOutcome(actor, operation, storage, ItemState.Refused, 0,
                        "the actor holds none of this item, so nothing was removed");
                }
                _counts[slot] = current - 1;
                break;
            default:
                if (current == 0)
                {
                    Refused++;
                    return new ItemOutcome(actor, operation, storage, ItemState.Refused, 0,
                        "the actor holds none of this item, so nothing was equipped");
                }
                _equipped[actor] = itemForm;
                break;
        }

        Applied++;
        return new ItemOutcome(actor, operation, storage, ItemState.Applied, CountOf(actor, itemForm),
            "the operation is proven and was applied");
    }

    /// <summary>Delivers an inventory event to the actors that registered for it.</summary>
    public ItemOutcome DeliverEvent(PapyrusOwnerId actor, string eventName, string itemFilter)
    {
        var registration = _registrations.MatchCustomEvent(eventName)
            .FirstOrDefault(entry => entry.Owner == actor);
        if (registration is null)
        {
            Refused++;
            return new ItemOutcome(actor, ItemOperation.Add, ItemStorage.Carried, ItemState.Refused, 0,
                "the actor did not register for this inventory event, so it was not delivered");
        }

        if (_filters.TryGetValue((actor, eventName), out var registeredFilter)
            && !string.Equals(registeredFilter, itemFilter, StringComparison.Ordinal))
        {
            Refused++;
            return new ItemOutcome(actor, ItemOperation.Add, ItemStorage.Carried, ItemState.Refused, 0,
                $"the registration's filter {registeredFilter} does not match {itemFilter}");
        }

        var batch = _bridge.Send(new EngineEvent(eventName, Array.Empty<PapyrusValue>(), actor));
        if (batch.Delivered > 0)
        {
            return new ItemOutcome(actor, ItemOperation.Add, ItemStorage.Carried, ItemState.Applied, 0,
                "the registered actor received the inventory event");
        }

        Refused++;
        return new ItemOutcome(actor, ItemOperation.Add, ItemStorage.Carried, ItemState.Refused, 0,
            batch.Outcomes.FirstOrDefault()?.Reason ?? "the event was not delivered");
    }
}
