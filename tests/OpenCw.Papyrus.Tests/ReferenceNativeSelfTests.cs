using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Items;
using OpenCommonwealth.Services.Papyrus.Magic;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ReferenceNativeSelfTests
{
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "First:Probe.esp");
    private static readonly PapyrusOwnerId Activator = PapyrusOwnerId.ForPlacedReference("Actor:Probe.esp", "Player:Probe.esp");
    private const string Metadata = "[{\"name\":\"ReferenceProbe\",\"properties\":[]}]";

    private static EvidenceGate Gate(string? denied = null)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.OnInitBarrier, EvidenceKeys.SchedulerBudget, EvidenceKeys.NativeTarget, EvidenceKeys.Dispatch,
            "vm.session.cell_lifecycle", "vm.session.activation_routing", "vmad.reference_compiled_attachments",
            "vmad.reference_source", "owner.engine_identity", "event.OnActivate", NativeWait.Key,
            "system.inventory", "system.magic_perk", InventoryRuntime.AddKey, MagicPerkRuntime.PerkKey };
        return new EvidenceGate(EvidenceRegistry.Load(System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = EvidenceRegistry.SchemaName,
            rows = keys.Select(key => new { key, status = key == denied ? "UNPROVEN" : "ENGINE_POLICY" }),
        })));
    }

    private static GameSession Session(PapyrusInstruction[] body, bool onInit = false)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("ObjectReference", new[]
        {
            new PapyrusFunction("Observe", Array.Empty<PapyrusInstruction>(), ParameterCount: 1, IsNative: true),
            new PapyrusFunction("Wait", Array.Empty<PapyrusInstruction>(), IsNative: true),
        }, Array.Empty<string>()));
        library.Add(new PapyrusScript("ReferenceProbe", new[]
        {
            new PapyrusFunction(onInit ? "OnInit" : "OnActivate", body, ParameterCount: onInit ? 0 : 1),
        }, Array.Empty<string>(), Parent: "ObjectReference"));
        return new GameSession(library, Gate());
    }

    private static PapyrusInstruction Observe() =>
        PapyrusInstruction.CallMethod(PapyrusOperand.SelfRef, "Observe", PapyrusOperand.Discard,
            new[] { PapyrusOperand.Constant(PapyrusValue.FromInt(7)) }, receiverType: "ObjectReference");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAttachedReferenceSuppliesItsExplicitOwnerToEngineNatives(bool onInit)
    {
        var session = Session(new[] { Observe(), PapyrusInstruction.Return() }, onInit);
        var observed = new List<EngineObjectReference>();
        session.Host.RegisterNative(new NativeId("ObjectReference", "Observe"), (receiver, arguments) =>
        {
            observed.Add(Assert.IsType<EngineObjectReference>(receiver.Reference));
            Assert.Equal(PapyrusValue.FromInt(7), Assert.Single(arguments));
            return PapyrusValue.None;
        }, NativeTargetKind.EngineObject);
        var attached = session.AttachReference("Cell", Owner, Metadata);
        Assert.Null(attached.Refusal);
        Assert.True(Assert.Single(attached.Initialization).Completed);
        Assert.Equal(Owner, Assert.Single(attached.Instances).Owner);
        if (!onInit) Assert.Equal(1, session.Activate(Owner, Activator).Delivered);
        var receiver = Assert.Single(observed);
        Assert.Equal(Owner, receiver.Owner);
        Assert.Equal(Owner.Key, receiver.Identity);
        Assert.Equal(0, ((ScriptHost)session.Host).NativeTargetRefusals);
    }

    [Fact]
    public void SelfAddItemKeepsTwoReferencesWithOneBaseFormIndependent()
    {
        const string item = "Item:Probe.esp";
        var session = Session(new[]
        {
            PapyrusInstruction.CallMethod(PapyrusOperand.SelfRef, "AddItem", PapyrusOperand.Discard,
                new[] { PapyrusOperand.Constant(PapyrusValue.FromObject(new EngineObjectReference(item))) },
                receiverType: "ObjectReference"),
            PapyrusInstruction.Return(),
        });
        var gate = Gate();
        var inventory = new InventoryRuntime(session.Events, new ScriptRegistrationRegistry(), gate);
        new GameplayNativeAdapters(session.Host, inventory, new MagicPerkRuntime(session.Host, gate), gate).Bind();
        var second = PapyrusOwnerId.ForPlacedReference(Owner.BaseFormKey, "Second:Probe.esp");
        session.AttachReference("Played", Owner, Metadata);
        session.AttachReference("Streamed", second, Metadata);
        Assert.Equal(1, session.Activate(Owner, Activator).Delivered);
        Assert.Equal(1, inventory.CountOf(Owner, item));
        Assert.Equal(0, inventory.CountOf(second, item));
        Assert.Equal(1, session.Activate(second, Activator).Delivered);
        Assert.Equal(1, inventory.CountOf(second, item));
        Assert.Equal(2, inventory.Applied);
        Assert.Equal(0, inventory.Refused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelfReceiverSurvivesLatentCompletionAndExactInstanceReattachment(bool completeFirst)
    {
        var session = Session(new[]
        {
            PapyrusInstruction.CallMethod(PapyrusOperand.SelfRef, "Wait", PapyrusOperand.Local(1),
                Array.Empty<PapyrusOperand>(), receiverType: "ObjectReference"),
            PapyrusInstruction.CallMethod(PapyrusOperand.SelfRef, "Observe", PapyrusOperand.Discard,
                new[] { PapyrusOperand.Local(1) }, receiverType: "ObjectReference"),
            PapyrusInstruction.Return(),
        });
        var observed = new List<PapyrusValue>();
        session.Host.RegisterNative(new NativeId("ObjectReference", "Wait"), (receiver, _) =>
        {
            Assert.Equal(Owner, Assert.IsType<EngineObjectReference>(receiver.Reference).Owner);
            throw new NativeWait(42);
        }, NativeTargetKind.EngineObject);
        session.Host.RegisterNative(new NativeId("ObjectReference", "Observe"), (receiver, arguments) =>
        {
            Assert.Equal(Owner, Assert.IsType<EngineObjectReference>(receiver.Reference).Owner);
            observed.Add(Assert.Single(arguments));
            return PapyrusValue.None;
        }, NativeTargetKind.EngineObject);
        var instance = Assert.Single(session.AttachReference("Cell", Owner, Metadata).Instances);
        Assert.Equal(1, session.Activate(Owner, Activator).Pending);
        if (completeFirst) Assert.True(session.Host.CompleteLatent(42, PapyrusValue.FromInt(9)));
        session.DetachCell("Cell");
        if (!completeFirst) Assert.True(session.Host.CompleteLatent(42, PapyrusValue.FromInt(9)));
        session.Host.RunStep(32);
        Assert.Empty(observed);
        Assert.Equal(1, session.Host.WaitingCount);
        session.ReattachCell("Cell");
        Assert.Same(instance, Assert.Single(session.AttachReference("Cell", Owner, Metadata).Instances));
        session.Host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(9), Assert.Single(observed));
        Assert.Equal(1, session.Events.Pending);
        Assert.Equal(0, session.Events.Delivered);
        Assert.Equal(0, session.Host.WaitingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptAndStaticRegistrationsStillReceiveTheOriginalScriptSelf(bool global)
    {
        var call = global ? PapyrusInstruction.CallStatic("External", "Observe", PapyrusOperand.Discard,
            Array.Empty<PapyrusOperand>()) : Observe();
        var session = Session(new[] { call, PapyrusInstruction.Return() });
        ScriptInstance? observed = null;
        session.Host.RegisterNative(new NativeId(global ? "External" : "ObjectReference", "Observe"), (self, _) =>
        {
            observed = Assert.IsType<ScriptInstance>(self.Reference);
            return PapyrusValue.None;
        }, global ? NativeTargetKind.Static : NativeTargetKind.ScriptInstance);
        var instance = Assert.Single(session.AttachReference("Cell", Owner, Metadata).Instances);
        Assert.Equal(1, session.Activate(Owner, Activator).Delivered);
        Assert.Same(instance, observed);
    }

    [Fact]
    public void AStandaloneScriptCannotFabricateAnEngineReceiver()
    {
        var session = Session(new[] { Observe(), PapyrusInstruction.Return() });
        var calls = 0;
        session.Host.RegisterNative(new NativeId("ObjectReference", "Observe"), (_, _) =>
        {
            calls++;
            return PapyrusValue.None;
        }, NativeTargetKind.EngineObject);
        var instance = session.Host.Attach("ReferenceProbe");
        Assert.True(session.Host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.ModelError, session.Host.RunEvent(instance, "OnActivate", PapyrusValue.None).Reason);
        Assert.Equal(0, calls);
        Assert.Equal(1, ((ScriptHost)session.Host).NativeTargetRefusals);
    }

    [Fact]
    public void AnInvalidOwnerCannotLeaveAnAttachedInstanceBehind()
    {
        var session = Session(new[] { PapyrusInstruction.Return() });
        Assert.Throws<ArgumentException>(() => session.Host.Attach("ReferenceProbe", owner: default(PapyrusOwnerId)));
        Assert.Empty(session.Host.Instances);
    }

    [Fact]
    public void ACellBindingCannotClaimAnotherOwnersReferenceKey()
    {
        var session = Session(new[] { PapyrusInstruction.Return() });
        Assert.Throws<ArgumentException>(() => session.Cells.Bind("Cell", "another-reference", "ReferenceProbe", owner: Owner));
        Assert.Empty(session.Host.Instances);
        Assert.Equal(0, session.Cells.BoundInstances);
    }

    [Fact]
    public void AnExplicitOwnerCannotBypassItsEvidenceGateAtNativeInvocation()
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("ReferenceProbe", new[]
        {
            new PapyrusFunction("OnActivate", new[] { Observe(), PapyrusInstruction.Return() }, ParameterCount: 1),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, Gate("owner.engine_identity"));
        var calls = 0;
        host.RegisterNative(new NativeId("ObjectReference", "Observe"), (_, _) =>
        {
            calls++;
            return PapyrusValue.None;
        }, NativeTargetKind.EngineObject);
        var instance = host.Attach("ReferenceProbe", owner: Owner);
        Assert.True(host.Initialize(instance).Completed);
        var result = host.RunEvent(instance, "OnActivate", PapyrusValue.None);
        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains("owner.engine_identity", result.Message);
        Assert.Equal(0, calls);
    }
}
