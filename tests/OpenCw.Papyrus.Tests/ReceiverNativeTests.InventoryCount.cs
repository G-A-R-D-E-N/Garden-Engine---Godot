using System.Reflection;
using OpenCommonwealth.Services.Papyrus.Items;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public partial class ReceiverNativeTests
{
    [Theory]
    [InlineData(false, "exact", "Actor", 2)]
    [InlineData(true, "exact", "Actor", 2)]
    [InlineData(false, "missing", "Actor", 0)]
    [InlineData(true, "missing", "Actor", 0)]
    [InlineData(false, "omitted", "Actor", 3)]
    [InlineData(true, "omitted", "Actor", 3)]
    [InlineData(false, "none", "Actor", 3)]
    [InlineData(true, "none", "Actor", 3)]
    [InlineData(false, "exact", "ObjectReference", 2)]
    public void GetItemCountReadsOnlyItsReceiversModeledInventory(bool dynamic, string query, string receiverType, int expected)
    {
        var owner = dynamic ? PapyrusOwnerId.ForDynamicReference(Actor.BaseFormKey, "created") : Actor;
        var arguments = query switch
        {
            "exact" => new[] { Item },
            "missing" => new[] { PapyrusValue.FromObject(new EngineObjectReference("missing")) },
            "none" => new[] { PapyrusValue.None },
            _ => Array.Empty<PapyrusValue>(),
        };
        var gate = Gate();
        var host = Host(gate, receiverType, "GetItemCount", PapyrusValue.FromObject(new EngineObjectReference(owner)), arguments);
        var (inventory, magic, report) = Bind(host, gate);
        SeedInventoryCounts(inventory, owner);

        var result = Run(host);

        Assert.True(result.Completed, result.Message);
        Assert.Equal(PapyrusValueKind.Int, result.Value.Kind);
        Assert.Equal(expected, result.Value.Integer);
        Assert.Equal(AdapterState.Bound, Assert.Single(report.Natives,
            entry => entry.Family.Native == new NativeId("ObjectReference", "GetItemCount")).State);
        AssertInventoryCountState(inventory, owner);
        Assert.Equal(0, magic.Applied);
    }

    [Theory]
    [InlineData("extra", "extra argument")]
    [InlineData("string", "engine object identity")]
    [InlineData("int", "engine object identity")]
    [InlineData("bool", "engine object identity")]
    [InlineData("blank", "engine object identity")]
    [InlineData("placed-item", "item reference")]
    [InlineData("dynamic-item", "item reference")]
    [InlineData("ownerless", "resolved Garden owner")]
    [InlineData("Form", "owner kind")]
    [InlineData("QuestAlias", "owner kind")]
    public void UnsupportedInventoryCountQueriesRefuseWithoutChangingState(string invalid, string reason)
    {
        var owner = invalid switch
        {
            "Form" => PapyrusOwnerId.ForForm(Actor.BaseFormKey),
            "QuestAlias" => PapyrusOwnerId.ForQuestAlias(Actor.BaseFormKey, 1),
            _ => Actor,
        };
        var receiver = invalid == "ownerless" ? PapyrusValue.FromObject(new EngineObjectReference("unknown"))
            : PapyrusValue.FromObject(new EngineObjectReference(owner));
        var arguments = invalid switch
        {
            "extra" => new[] { Item, Item },
            "string" => new[] { PapyrusValue.FromString("item") },
            "int" => new[] { PapyrusValue.FromInt(1) },
            "bool" => new[] { PapyrusValue.FromBool(false) },
            "blank" => new[] { PapyrusValue.FromObject(new EngineObjectReference("")) },
            "placed-item" => new[] { PapyrusValue.FromObject(new EngineObjectReference(
                PapyrusOwnerId.ForPlacedReference("000900:Items.esp", "item"))) },
            "dynamic-item" => new[] { PapyrusValue.FromObject(new EngineObjectReference(
                PapyrusOwnerId.ForDynamicReference("000900:Items.esp", "item"))) },
            _ => new[] { Item },
        };
        var gate = Gate();
        var host = Host(gate, "Actor", "GetItemCount", receiver, arguments);
        var (inventory, _, _) = Bind(host, gate);
        SeedInventoryCounts(inventory, Actor);

        var result = Run(host);

        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains(reason, result.Message!, StringComparison.Ordinal);
        AssertInventoryCountState(inventory, Actor);
    }

    [Fact]
    public void InventoryCountTotalOverflowFailsWithoutChangingCounts()
    {
        var gate = Gate();
        var host = Host(gate, "Actor", "GetItemCount", Receiver());
        var (inventory, _, _) = Bind(host, gate);
        var field = typeof(InventoryRuntime).GetField("_counts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var counts = Assert.IsType<Dictionary<(PapyrusOwnerId Actor, string Item), int>>(field.GetValue(inventory));
        counts[(Actor, "first")] = int.MaxValue;
        counts[(Actor, "second")] = 1;

        var result = Run(host);

        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains("overflow", result.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(int.MaxValue, inventory.CountOf(Actor, "first"));
        Assert.Equal(1, inventory.CountOf(Actor, "second"));
        Assert.Equal(0, inventory.Applied);
        Assert.Equal(0, inventory.Refused);
    }

    [Fact]
    public void AnUnprovenInventorySystemKeepsCountQueriesUnbound()
    {
        var gate = Gate("system.inventory");
        var host = Host(gate, "Actor", "GetItemCount", Receiver(), Item);
        var (inventory, _, report) = Bind(host, gate);
        Assert.Equal(AdapterState.AbsentSystem, Assert.Single(report.Natives,
            entry => entry.Family.Native == new NativeId("ObjectReference", "GetItemCount")).State);
        Assert.Equal(VmStopReason.ModelError, Run(host).Reason);
        Assert.Equal(0, inventory.Applied);
    }

    [Fact]
    public void InventoryCountRegistrationConflictPreservesTheExistingHandler()
    {
        var gate = Gate();
        var host = Host(gate, "Actor", "GetItemCount", Receiver(), Item);
        var native = new NativeId("ObjectReference", "GetItemCount");
        host.RegisterNative(native, (_, _) => PapyrusValue.FromInt(17), NativeTargetKind.EngineObject);
        var (inventory, _, report) = Bind(host, gate);
        Assert.Equal(AdapterState.Conflict, Assert.Single(report.Natives, entry => entry.Family.Native == native).State);
        Assert.Equal(17, Run(host).Value.Integer);
        Assert.Equal(0, inventory.Applied);
    }

    private static void SeedInventoryCounts(InventoryRuntime inventory, PapyrusOwnerId owner)
    {
        var other = PapyrusOwnerId.ForPlacedReference(Actor.BaseFormKey, "other");
        inventory.Apply(owner, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);
        inventory.Apply(owner, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);
        inventory.Apply(owner, "000901:Items.esp", ItemOperation.Add, ItemStorage.Carried);
        inventory.Apply(other, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);
        inventory.Apply(owner, "000900:Items.esp", ItemOperation.Equip, ItemStorage.Equipped);
    }

    private static void AssertInventoryCountState(InventoryRuntime inventory, PapyrusOwnerId owner)
    {
        Assert.Equal(2, inventory.CountOf(owner, "000900:Items.esp"));
        Assert.Equal(1, inventory.CountOf(owner, "000901:Items.esp"));
        Assert.Equal(1, inventory.CountOf(PapyrusOwnerId.ForPlacedReference(Actor.BaseFormKey, "other"), "000900:Items.esp"));
        Assert.Equal("000900:Items.esp", inventory.EquippedOf(owner));
        Assert.Equal(5, inventory.Applied);
        Assert.Equal(0, inventory.Refused);
        Assert.Equal(0, inventory.UnsupportedStorage);
    }
}
