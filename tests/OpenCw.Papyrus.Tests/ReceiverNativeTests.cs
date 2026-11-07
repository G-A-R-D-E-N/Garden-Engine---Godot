using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Items;
using OpenCommonwealth.Services.Papyrus.Magic;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public partial class ReceiverNativeTests
{
    private static readonly PapyrusOwnerId Actor = PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000A00");
    private static readonly PapyrusValue Item = PapyrusValue.FromObject(new EngineObjectReference("000900:Items.esp"));

    private static EvidenceGate Gate(params string[] blocked)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.OnInitBarrier, EvidenceKeys.NativeTarget, EvidenceKeys.Dispatch,
            InventoryRuntime.AddKey, InventoryRuntime.RemoveKey, InventoryRuntime.EquipKey,
            MagicPerkRuntime.PerkKey, "system.inventory", "system.magic_perk" };
        var rows = keys.Select(key => new { key, status = blocked.Contains(key) ? "UNPROVEN" : "ENGINE_POLICY" });
        return new EvidenceGate(EvidenceRegistry.Load(System.Text.Json.JsonSerializer.Serialize(
            new { schema = EvidenceRegistry.SchemaName, rows })));
    }

    private static ScriptHost Host(EvidenceGate gate, string script, string function, PapyrusValue receiver,
        params PapyrusValue[] arguments)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Caller", new[] { new PapyrusFunction("Run", new[]
        {
            PapyrusInstruction.CallMethod(PapyrusOperand.Constant(receiver), function, PapyrusOperand.Local(0),
                arguments.Select(PapyrusOperand.Constant).ToArray(), receiverType: script),
            PapyrusInstruction.Return(PapyrusOperand.Local(0)),
        }) }, Array.Empty<string>()));
        library.Add(new PapyrusScript("Actor", Array.Empty<PapyrusFunction>(), Array.Empty<string>(), "ObjectReference"));
        library.Add(new PapyrusScript("ObjectReference", Array.Empty<PapyrusFunction>(), Array.Empty<string>()));
        return new ScriptHost(library, gate);
    }

    private static PapyrusValue Receiver() => PapyrusValue.FromObject(new EngineObjectReference(Actor));

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void EquipItemUsesTheActorReceiverAndDefaultFlags(bool dynamic, int count)
    {
        var owner = dynamic ? PapyrusOwnerId.ForDynamicReference(Actor.BaseFormKey, "created") : Actor;
        var arguments = new[] { Item, PapyrusValue.FromBool(false), PapyrusValue.FromBool(false) }.Take(count).ToArray();
        var gate = Gate();
        var host = Host(gate, "Actor", "EquipItem", PapyrusValue.FromObject(new EngineObjectReference(owner)), arguments);
        var (inventory, _, _) = Bind(host, gate);
        inventory.Apply(owner, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);
        inventory.Apply(owner, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);
        var other = PapyrusOwnerId.ForPlacedReference(Actor.BaseFormKey, "other");
        inventory.Apply(other, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);
        var result = Run(host);
        Assert.True(result.Completed, result.Message);
        Assert.Equal(PapyrusValue.None, result.Value);
        Assert.Equal("000900:Items.esp", inventory.EquippedOf(owner));
        Assert.Null(inventory.EquippedOf(other));
        Assert.Equal(2, inventory.CountOf(owner, "000900:Items.esp"));
        Assert.Equal(1, inventory.CountOf(other, "000900:Items.esp"));
        Assert.Equal(4, inventory.Applied);
    }

    [Theory]
    [InlineData("prevent", "removal-prevention")]
    [InlineData("silent", "notification")]
    [InlineData("flag-type", "removal-prevention")]
    [InlineData("item-type", "engine object identity")]
    [InlineData("extra", "extra argument")]
    [InlineData("Form", "owner kind")]
    [InlineData("QuestAlias", "owner kind")]
    [InlineData("item-reference", "item reference")]
    [InlineData("gate", "refused")]
    [InlineData("empty", "refused")]
    public void UnsupportedEquipItemCallsStopBeforeMutation(string invalid, string reason)
    {
        var gate = invalid == "gate" ? Gate(InventoryRuntime.EquipKey) : Gate();
        var owner = invalid switch
        {
            "Form" => PapyrusOwnerId.ForForm(Actor.BaseFormKey),
            "QuestAlias" => PapyrusOwnerId.ForQuestAlias(Actor.BaseFormKey, 1),
            _ => Actor,
        };
        var arguments = invalid switch
        {
            "prevent" => new[] { Item, PapyrusValue.FromBool(true) },
            "silent" => new[] { Item, PapyrusValue.FromBool(false), PapyrusValue.FromBool(true) },
            "flag-type" => new[] { Item, PapyrusValue.FromInt(0) },
            "item-type" => new[] { PapyrusValue.FromString("item") },
            "extra" => new[] { Item, PapyrusValue.FromBool(false), PapyrusValue.FromBool(false), Item },
            "item-reference" => new[] { Receiver() },
            _ => new[] { Item },
        };
        var host = Host(gate, "Actor", "EquipItem", PapyrusValue.FromObject(new EngineObjectReference(owner)), arguments);
        var (inventory, _, _) = Bind(host, gate);
        if (invalid != "empty") inventory.Apply(owner, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);
        var result = Run(host);
        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains(reason, result.Message!);
        Assert.Equal(invalid == "empty" ? 0 : 1, inventory.CountOf(owner, "000900:Items.esp"));
        Assert.Equal(invalid == "empty" ? 0 : 1, inventory.Applied);
        Assert.Equal(invalid is "gate" or "empty" ? 1 : 0, inventory.Refused);
        Assert.Null(inventory.EquippedOf(owner));
    }

    private static VmResult Run(ScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    private static (InventoryRuntime Inventory, MagicPerkRuntime Magic, AdapterReport Report) Bind(
        ScriptHost host, EvidenceGate gate)
    {
        var inventory = new InventoryRuntime(new PapyrusEventBridge(host, gate), new ScriptRegistrationRegistry(), gate);
        var magic = new MagicPerkRuntime(host, gate);
        return (inventory, magic, new GameplayNativeAdapters(host, inventory, magic, gate).Bind());
    }

    [Theory]
    [InlineData("Actor", false, 1)]
    [InlineData("Actor", false, 2)]
    [InlineData("Actor", false, 3)]
    [InlineData("Actor", false, 4)]
    [InlineData("ObjectReference", true, 1)]
    [InlineData("ObjectReference", true, 4)]
    public void RemoveItemUsesTheReceiverInventoryAndDefaultArguments(string script, bool dynamic, int count)
    {
        var owner = dynamic ? PapyrusOwnerId.ForDynamicReference(Actor.BaseFormKey, "created") : Actor;
        var arguments = new[] { Item, PapyrusValue.FromInt(1), PapyrusValue.FromBool(false), PapyrusValue.None }
            .Take(count).ToArray();
        var gate = Gate();
        var host = Host(gate, script, "RemoveItem", PapyrusValue.FromObject(new EngineObjectReference(owner)), arguments);
        var (inventory, _, report) = Bind(host, gate);
        inventory.Apply(owner, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);
        inventory.Apply(owner, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);
        var other = PapyrusOwnerId.ForPlacedReference(Actor.BaseFormKey, "other");
        inventory.Apply(other, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);

        var result = Run(host);

        Assert.True(result.Completed, result.Message);
        Assert.Equal(PapyrusValue.None, result.Value);
        Assert.Equal(1, inventory.CountOf(owner, "000900:Items.esp"));
        Assert.Equal(1, inventory.CountOf(other, "000900:Items.esp"));
        Assert.Equal(4, inventory.Applied);
        Assert.Equal(AdapterState.Bound, report.Natives.Single(native => native.Family.Native.Function == "RemoveItem").State);
    }

    [Theory]
    [InlineData("transfer", "container")]
    [InlineData("count", "count of one")]
    [InlineData("silent", "notification")]
    [InlineData("extra", "extra argument")]
    [InlineData("Form", "owner kind")]
    [InlineData("QuestAlias", "owner kind")]
    [InlineData("item-reference", "item reference")]
    public void UnsupportedRemoveItemCallsStopBeforeMutation(string invalid, string reason)
    {
        var gate = Gate();
        var owner = invalid switch
        {
            "Form" => PapyrusOwnerId.ForForm(Actor.BaseFormKey),
            "QuestAlias" => PapyrusOwnerId.ForQuestAlias(Actor.BaseFormKey, 1),
            _ => Actor,
        };
        var arguments = invalid switch
        {
            "transfer" => new[] { Item, PapyrusValue.FromInt(1), PapyrusValue.FromBool(false), Receiver() },
            "count" => new[] { Item, PapyrusValue.FromInt(2) },
            "silent" => new[] { Item, PapyrusValue.FromInt(1), PapyrusValue.FromBool(true) },
            "extra" => new[] { Item, PapyrusValue.FromInt(1), PapyrusValue.FromBool(false), PapyrusValue.None, Item },
            "item-reference" => new[] { Receiver() },
            _ => new[] { Item },
        };
        var host = Host(gate, "Actor", "RemoveItem", PapyrusValue.FromObject(new EngineObjectReference(owner)), arguments);
        var (inventory, _, _) = Bind(host, gate);
        inventory.Apply(owner, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);

        var result = Run(host);

        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains(reason, result.Message!);
        Assert.Equal(1, inventory.CountOf(owner, "000900:Items.esp"));
        Assert.Equal(1, inventory.Applied);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemoveItemRetainsOperationAndEmptyInventoryRefusals(bool blocked)
    {
        var gate = blocked ? Gate(InventoryRuntime.RemoveKey) : Gate();
        var host = Host(gate, "Actor", "RemoveItem", Receiver(), Item);
        var (inventory, _, _) = Bind(host, gate);
        if (blocked) inventory.Apply(Actor, "000900:Items.esp", ItemOperation.Add, ItemStorage.Carried);

        var result = Run(host);

        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains("refused", result.Message!);
        Assert.Equal(blocked ? 1 : 0, inventory.CountOf(Actor, "000900:Items.esp"));
        Assert.Equal(blocked ? 1 : 0, inventory.Applied);
        Assert.Equal(1, inventory.Refused);
    }

    [Fact]
    public void AReceiverNativeGetsTheValidatedReceiverThroughBytecode()
    {
        var receiver = PapyrusValue.FromObject(new EngineObjectReference("000800:Npcs.esp"));
        var host = Host(Gate(), "Actor", "Echo", receiver, Item);
        PapyrusValue seen = default;
        Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> body = (target, args) =>
        {
            seen = target;
            Assert.Equal(Item, Assert.Single(args));
            return PapyrusValue.FromInt(17);
        };
        host.RegisterNative(new NativeId("Actor", "Echo"), body, NativeTargetKind.EngineObject);

        var result = Run(host);

        Assert.True(result.Completed, result.Message);
        Assert.Equal(receiver, seen);
        Assert.Equal(17, result.Value.Integer);
    }

    [Fact]
    public void TryRegisterReceiverNativeDoesNotOverwriteABody()
    {
        var receiver = PapyrusValue.FromObject(new EngineObjectReference("000800:Npcs.esp"));
        var host = Host(Gate(), "Actor", "Echo", receiver);
        Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> first = (_, _) => PapyrusValue.FromInt(1);
        Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> second = (_, _) => PapyrusValue.FromInt(2);
        Assert.True(host.TryRegisterNative(new NativeId("Actor", "Echo"), first, NativeTargetKind.EngineObject));
        Assert.False(host.TryRegisterNative(new NativeId("Actor", "Echo"), second, NativeTargetKind.EngineObject));
        Assert.Equal(1, Run(host).Value.Integer);
    }

    [Fact]
    public void ArgumentOnlyRegistrationsKeepTheirArgumentsAndResult()
    {
        var host = Host(Gate(), "Actor", "Echo", PapyrusValue.None);
        host.RegisterNative(new NativeId("Actor", "Echo"), args => Assert.Single(args), NativeTargetKind.EngineObject);
        var result = ((INativeTarget)host).Invoke(new NativeId("Actor", "Echo"), NativeTargetKind.EngineObject,
            PapyrusValue.FromObject(new EngineObjectReference("actor")), new[] { Item });
        Assert.Equal(Item, result);
    }

    [Theory]
    [InlineData("AddItem", "ObjectReference")]
    [InlineData("RemoveItem", "ObjectReference")]
    [InlineData("EquipItem", "Actor")]
    [InlineData("AddPerk", "Actor")]
    public void AdapterBindingPreservesExistingHandlersAndReportsConflicts(string function, string script)
    {
        var gate = Gate();
        var host = Host(gate, "Actor", function, Receiver(), Item);
        var calls = 0;
        host.RegisterNative(new NativeId(script, function), (receiver, arguments) =>
        {
            Assert.Equal(Receiver(), receiver);
            Assert.Equal(Item, Assert.Single(arguments));
            calls++;
            return PapyrusValue.FromInt(17);
        }, NativeTargetKind.EngineObject);
        var (inventory, magic, report) = Bind(host, gate);

        Assert.Equal(17, Run(host).Value.Integer);
        Assert.Equal(1, calls);
        Assert.Equal(0, inventory.Applied);
        Assert.Equal(0, magic.Applied);
        var outcome = report.Natives.Single(native => native.Family.Native.Function == function);
        Assert.NotEqual(AdapterState.Bound, outcome.State);
        Assert.Contains("conflict", outcome.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, report.Bound);
        Assert.Equal(7, report.Unbound);
    }

    [Fact]
    public void RepeatedBindingKeepsTheFirstAdapterRuntimes()
    {
        var gate = Gate();
        var host = Host(gate, "Actor", "AddItem", Receiver(), Item);
        var (inventory, magic, _) = Bind(host, gate);
        var secondInventory = new InventoryRuntime(new PapyrusEventBridge(host, gate), new ScriptRegistrationRegistry(), gate);
        var secondMagic = new MagicPerkRuntime(host, gate);
        var adapters = new GameplayNativeAdapters(host, secondInventory, secondMagic, gate);
        var report = adapters.Bind();

        Assert.True(Run(host).Completed);
        Assert.Equal(1, inventory.Applied);
        Assert.Equal(0, magic.Applied);
        Assert.Equal(0, secondInventory.Applied);
        Assert.Equal(0, secondMagic.Applied);
        Assert.Equal(0, report.Bound);
        Assert.Equal(11, report.Unbound);
        Assert.Equal(0, adapters.Bound);
        Assert.Equal(11, adapters.Unbound);
        Assert.Equal(5, report.Natives.Count(native => native.Reason.Contains("conflict", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("AddItem", PapyrusOwnerKind.PlacedReference, true)]
    [InlineData("AddPerk", PapyrusOwnerKind.PlacedReference, true)]
    [InlineData("AddItem", PapyrusOwnerKind.DynamicReference, true)]
    [InlineData("AddPerk", PapyrusOwnerKind.DynamicReference, true)]
    [InlineData("AddItem", PapyrusOwnerKind.Form, false)]
    [InlineData("AddPerk", PapyrusOwnerKind.Form, false)]
    [InlineData("AddItem", PapyrusOwnerKind.QuestAlias, false)]
    [InlineData("AddPerk", PapyrusOwnerKind.QuestAlias, false)]
    [InlineData("AddItem", PapyrusOwnerKind.RuntimeInstance, false)]
    [InlineData("AddPerk", PapyrusOwnerKind.RuntimeInstance, false)]
    public void OnlyReferenceOwnersCanMutateInventoryOrPerks(string function, PapyrusOwnerKind kind, bool accepted)
    {
        var owner = kind switch
        {
            PapyrusOwnerKind.PlacedReference => Actor,
            PapyrusOwnerKind.DynamicReference => PapyrusOwnerId.ForDynamicReference(Actor.BaseFormKey, "created"),
            PapyrusOwnerKind.Form => PapyrusOwnerId.ForForm(Actor.BaseFormKey),
            PapyrusOwnerKind.QuestAlias => PapyrusOwnerId.ForQuestAlias(Actor.BaseFormKey, 1),
            _ => PapyrusOwnerId.ForRuntimeInstance(Actor.BaseFormKey, 1),
        };
        var gate = Gate();
        var host = Host(gate, "Actor", function, PapyrusValue.FromObject(new EngineObjectReference(owner)), Item);
        var (inventory, magic, _) = Bind(host, gate);

        var result = Run(host);

        Assert.Equal(accepted ? VmStopReason.Completed : VmStopReason.ModelError, result.Reason);
        if (!accepted) Assert.Contains("owner kind", result.Message!);
        Assert.Equal(accepted && function == "AddItem" ? 1 : 0, inventory.Applied);
        Assert.Equal(accepted && function == "AddPerk" ? 1 : 0, magic.Applied);
        Assert.Equal(accepted && function == "AddItem" ? 1 : 0, inventory.CountOf(owner, "000900:Items.esp"));
        Assert.Equal(accepted && function == "AddPerk", magic.HasPerk(owner, "000900:Items.esp"));
    }

    [Theory]
    [InlineData("Actor", "AddItem")]
    [InlineData("ObjectReference", "AddItem")]
    [InlineData("Actor", "AddPerk")]
    public void AdaptersApplyToTheReceiverOwnerAndReturnNone(string script, string function)
    {
        var gate = Gate();
        var host = Host(gate, script, function, Receiver(), Item);
        var (inventory, magic, report) = Bind(host, gate);
        Assert.Equal(5, report.Bound);
        Assert.Equal(6, report.Unbound);

        var result = Run(host);

        Assert.True(result.Completed, result.Message);
        Assert.Equal(PapyrusValue.None, result.Value);
        Assert.Equal(function == "AddItem" ? 1 : 0, inventory.CountOf(Actor, "000900:Items.esp"));
        Assert.Equal(function == "AddPerk", magic.HasPerk(Actor, "000900:Items.esp"));
        Assert.Equal(0, inventory.CountOf(PapyrusOwnerId.ForPlacedReference(Actor.BaseFormKey, "other"), "000900:Items.esp"));
    }

    [Theory]
    [InlineData("AddItem", "receiver")]
    [InlineData("AddPerk", "receiver")]
    [InlineData("AddItem", "missing")]
    [InlineData("AddPerk", "missing")]
    [InlineData("AddItem", "type")]
    [InlineData("AddPerk", "type")]
    [InlineData("AddItem", "count")]
    [InlineData("AddItem", "count-type")]
    [InlineData("AddItem", "flag-type")]
    [InlineData("AddPerk", "flag-type")]
    [InlineData("AddItem", "extra")]
    [InlineData("AddPerk", "extra")]
    [InlineData("AddPerk", "notify")]
    [InlineData("AddItem", "unresolved")]
    [InlineData("AddItem", "placed-item")]
    [InlineData("AddItem", "dynamic-item")]
    [InlineData("AddPerk", "placed-item")]
    [InlineData("AddPerk", "dynamic-item")]
    public void InvalidCallsStopBeforeStateChanges(string function, string invalid)
    {
        var gate = Gate();
        var receiver = invalid == "receiver" ? PapyrusValue.FromString("actor")
            : invalid == "unresolved" ? PapyrusValue.FromObject(new EngineObjectReference("actor")) : Receiver();
        var arguments = invalid switch
        {
            "missing" => Array.Empty<PapyrusValue>(),
            "type" => new[] { PapyrusValue.FromString("item") },
            "count" => new[] { Item, PapyrusValue.FromInt(2) },
            "count-type" => new[] { Item, PapyrusValue.FromBool(true) },
            "flag-type" when function == "AddItem" => new[] { Item, PapyrusValue.FromInt(1), PapyrusValue.FromInt(0) },
            "flag-type" => new[] { Item, PapyrusValue.FromInt(0) },
            "notify" => new[] { Item, PapyrusValue.FromBool(true) },
            "extra" => new[] { Item, PapyrusValue.FromInt(1), PapyrusValue.FromBool(false), Item },
            "placed-item" => new[] { PapyrusValue.FromObject(new EngineObjectReference(
                PapyrusOwnerId.ForPlacedReference("000900:Items.esp", "item"))) },
            "dynamic-item" => new[] { PapyrusValue.FromObject(new EngineObjectReference(
                PapyrusOwnerId.ForDynamicReference("000900:Items.esp", "item"))) },
            _ => new[] { Item },
        };
        var host = Host(gate, "Actor", function, receiver, arguments);
        var (inventory, magic, report) = Bind(host, gate);
        Assert.Equal(5, report.Bound);

        var result = Run(host);

        Assert.Equal(0, inventory.Applied);
        Assert.Equal(0, magic.Applied);
        Assert.Equal(VmStopReason.ModelError, result.Reason);
        if (invalid is "placed-item" or "dynamic-item") Assert.Contains("item reference", result.Message!);
    }

    [Theory]
    [InlineData("AddItem", InventoryRuntime.AddKey)]
    [InlineData("AddPerk", MagicPerkRuntime.PerkKey)]
    public void RuntimeRejectionsStopTheCall(string function, string key)
    {
        var gate = Gate(key);
        var host = Host(gate, "Actor", function, Receiver(), Item);
        var (inventory, magic, report) = Bind(host, gate);
        Assert.Equal(5, report.Bound);
        var result = Run(host);
        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains("refused", result.Message!);
        Assert.Equal(0, inventory.Applied);
        Assert.Equal(0, magic.Applied);
    }

    [Fact]
    public void MissingSystemKeysLeaveEveryNativeUnbound()
    {
        var gate = Gate("system.inventory", "system.magic_perk");
        var host = Host(gate, "Actor", "AddItem", PapyrusValue.None);
        var (_, _, report) = Bind(host, gate);
        Assert.Equal(0, report.Bound);
        Assert.Equal(11, report.Unbound);
        Assert.Equal(VmStopReason.ModelError, Run(host).Reason);
    }

    [Fact]
    public void AnIdentityThatDisagreesWithItsOwnerStopsBeforeMutation()
    {
        var gate = Gate();
        var reference = Assert.IsType<EngineObjectReference>(Receiver().Reference) with { Identity = "another actor" };
        var host = Host(gate, "Actor", "AddItem", PapyrusValue.FromObject(reference), Item);
        var (inventory, _, _) = Bind(host, gate);
        Assert.Equal(VmStopReason.ModelError, Run(host).Reason);
        Assert.Equal(0, inventory.Applied);
    }

    [Theory]
    [InlineData("AddItem")]
    [InlineData("AddPerk")]
    public void ExplicitDefaultArgumentsAreAccepted(string function)
    {
        var gate = Gate();
        var arguments = function == "AddItem"
            ? new[] { Item, PapyrusValue.FromInt(1), PapyrusValue.FromBool(false) }
            : new[] { Item, PapyrusValue.FromBool(false) };
        var host = Host(gate, "Actor", function, Receiver(), arguments);
        Bind(host, gate);
        Assert.True(Run(host).Completed);
    }

    [Fact]
    public void APerkRejectedAsDuplicateStopsWithoutInventingSuccess()
    {
        var gate = Gate();
        var host = Host(gate, "Actor", "AddPerk", Receiver(), Item);
        var (_, magic, _) = Bind(host, gate);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.True(host.RunEvent(instance, "Run").Completed);
        var duplicate = host.RunEvent(instance, "Run");
        Assert.Equal(VmStopReason.ModelError, duplicate.Reason);
        Assert.Contains("refused", duplicate.Message!);
        Assert.True(magic.HasPerk(Actor, "000900:Items.esp"));
        Assert.Equal(1, magic.Applied);
        Assert.Equal(1, magic.Refused);
    }
}
