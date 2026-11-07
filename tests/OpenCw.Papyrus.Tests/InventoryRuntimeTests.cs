using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Items;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class InventoryRuntimeTests
{
    private const string Base = "000800:Npcs.esp";
    private const string Item = "000900:Items.esp";

    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (InventoryRuntime.AddKey, "PROVEN"),
        (InventoryRuntime.RemoveKey, "PROVEN"),
        (InventoryRuntime.EquipKey, "PROVEN"),
        (InventoryRuntime.StorageKey, "UNPROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnItemAdded", "PROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = Rows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static PapyrusOwnerId Actor(string reference) => PapyrusOwnerId.ForPlacedReference(Base, reference);

    private static (InventoryRuntime Runtime, PapyrusEventBridge Bridge, ScriptRegistrationRegistry Registrations) Setup(
        EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("ItemScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnItemAdded", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var bridge = new PapyrusEventBridge(host, gate);
        var registrations = new ScriptRegistrationRegistry();
        var instance = host.Attach("ItemScript");
        host.Initialize(instance);
        bridge.Register(Actor("000A00"), instance);
        return (new InventoryRuntime(bridge, registrations, gate), bridge, registrations);
    }

    [Fact]
    public void WorldAndInventoryOwnersAreDistinct()
    {
        var world = InventoryRuntime.WorldItem(Item, "000A10");
        var carried = InventoryRuntime.InventoryItem(Actor("000A00"), Item, 1);

        Assert.NotEqual(world, carried);
        Assert.StartsWith("placed:", world.Key, StringComparison.Ordinal);
        Assert.StartsWith("dynamic:", carried.Key, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoStacksOfOneItemAreDistinctOwners()
    {
        var first = InventoryRuntime.InventoryItem(Actor("000A00"), Item, 1);
        var second = InventoryRuntime.InventoryItem(Actor("000A00"), Item, 2);

        Assert.NotEqual(first, second);
        Assert.Contains("#1", first.Key, StringComparison.Ordinal);
        Assert.Contains("#2", second.Key, StringComparison.Ordinal);
    }

    [Fact]
    public void AddAndRemoveChangeTheAuthoritativeCount()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);

        Assert.Equal(ItemState.Applied, runtime.Apply(Actor("000A00"), Item, ItemOperation.Add, ItemStorage.Carried).State);
        runtime.Apply(Actor("000A00"), Item, ItemOperation.Add, ItemStorage.Carried);
        Assert.Equal(2, runtime.CountOf(Actor("000A00"), Item));

        Assert.Equal(ItemState.Applied, runtime.Apply(Actor("000A00"), Item, ItemOperation.Remove, ItemStorage.Carried).State);
        Assert.Equal(1, runtime.CountOf(Actor("000A00"), Item));
        Assert.Equal(3, runtime.Applied);
    }

    [Fact]
    public void RemovingMoreThanHeldIsRefused()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);

        var outcome = runtime.Apply(Actor("000A00"), Item, ItemOperation.Remove, ItemStorage.Carried);

        Assert.Equal(ItemState.Refused, outcome.State);
        Assert.Equal(0, runtime.CountOf(Actor("000A00"), Item));
        Assert.Equal(1, runtime.Refused);
    }

    [Fact]
    public void EquipRecordsTheEquippedItem()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);
        runtime.Apply(Actor("000A00"), Item, ItemOperation.Add, ItemStorage.Carried);

        var outcome = runtime.Apply(Actor("000A00"), Item, ItemOperation.Equip, ItemStorage.Equipped);

        Assert.Equal(ItemState.Applied, outcome.State);
        Assert.Equal(Item, runtime.EquippedOf(Actor("000A00")));
    }

    [Fact]
    public void ContainerStorageIsExplicitlyUnsupported()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);

        var outcome = runtime.Apply(Actor("000A00"), Item, ItemOperation.Add, ItemStorage.Container);

        Assert.Equal(ItemState.UnsupportedStorage, outcome.State);
        Assert.Contains("not proven", outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(1, runtime.UnsupportedStorage);
        Assert.Equal(0, runtime.CountOf(Actor("000A00"), Item));
    }

    [Fact]
    public void AnUnprovenOperationChangesNothing()
    {
        var gate = Gate((InventoryRuntime.AddKey, "UNPROVEN"));
        var (runtime, _, _) = Setup(gate);

        var outcome = runtime.Apply(Actor("000A00"), Item, ItemOperation.Add, ItemStorage.Carried);

        Assert.Equal(ItemState.Refused, outcome.State);
        Assert.Equal(0, runtime.CountOf(Actor("000A00"), Item));
        Assert.Equal(1, runtime.Refused);
    }

    [Fact]
    public void AnInventoryEventNeedsARegistrationAndAMatchingFilter()
    {
        var gate = Gate();
        var (runtime, _, registrations) = Setup(gate);
        registrations.Register(Actor("000A00"), ScriptRegistration.ForCustomEvent("OnItemAdded"));
        runtime.SetFilter(Actor("000A00"), "OnItemAdded", Item);

        var filtered = runtime.DeliverEvent(Actor("000A00"), "OnItemAdded", "000900:Other.esp");
        Assert.Equal(ItemState.Refused, filtered.State);
        Assert.Contains("does not match", filtered.Reason, StringComparison.Ordinal);

        var delivered = runtime.DeliverEvent(Actor("000A00"), "OnItemAdded", Item);
        Assert.Equal(ItemState.Applied, delivered.State);

        var unregistered = runtime.DeliverEvent(Actor("000A01"), "OnItemAdded", Item);
        Assert.Equal(ItemState.Refused, unregistered.State);
        Assert.Contains("did not register", unregistered.Reason, StringComparison.Ordinal);
    }
}
