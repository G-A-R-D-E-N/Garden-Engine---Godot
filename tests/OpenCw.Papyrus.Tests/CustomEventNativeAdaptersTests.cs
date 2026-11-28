using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class CustomEventNativeAdaptersTests : HostFixture
{
    private static readonly ScriptSpec ObjectSpec = TestHosts.Stub("ScriptObject", null,
        "Function RegisterForCustomEvent(ScriptObject akSender, String asEventName) native",
        "Function UnregisterForCustomEvent(ScriptObject akSender, String asEventName) native",
        "Function SendCustomEvent(String asEvent, Var[] akArgs = None) native");

    private static readonly ScriptSpec EmitterSpec = new("Emitter",
        "Scriptname Emitter extends ScriptObject\n" +
        "CustomEvent Tick\n" +
        "Function Fire(Int aiValue)\n" +
        "  Var[] args = new Var[1]\n" +
        "  args[0] = aiValue\n" +
        "  SendCustomEvent(\"emitter_Tick\", args)\n" +
        "EndFunction\n" +
        "Function FireBare()\n" +
        "  SendCustomEvent(\"emitter_Tick\")\n" +
        "EndFunction\n");

    private static readonly ScriptSpec ListenerSpec = new("Listener",
        "Scriptname Listener extends ScriptObject\n" +
        "ScriptObject Property Sender Auto\n" +
        "Int Property Id Auto\n" +
        "Function Record(Int aiWho, Int aiValue) global native\n" +
        "Event OnInit()\n" +
        "  RegisterForCustomEvent(Sender, \"emitter_Tick\")\n" +
        "EndEvent\n" +
        "Event Emitter.Tick(Emitter akSender, Var[] akArgs)\n" +
        "  If akArgs\n" +
        "    Record(Id, akArgs[0] as Int)\n" +
        "  Else\n" +
        "    Record(Id, -1)\n" +
        "  EndIf\n" +
        "EndEvent\n" +
        "Function Quit()\n" +
        "  UnregisterForCustomEvent(Sender, \"emitter_Tick\")\n" +
        "EndFunction\n");

    private const string SenderKey = "Q9:Test.esm";

    private sealed record Rig(RadicleScriptHost Host, CustomEventRegistry Registry, ScriptInstance Emitter, List<(long Who, long Value)> Heard);

    private Rig Setup()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), ObjectSpec, EmitterSpec, ListenerSpec));
        var registry = new CustomEventRegistry();
        Assert.Equal(3, new CustomEventNativeAdapters(host, TestHosts.DefaultGate(), registry).Bind().Bound);
        var heard = new List<(long, long)>();
        host.RegisterNative(new NativeId("Listener", "Record"), arguments =>
        {
            heard.Add((arguments[0].Integer, arguments[1].Integer));
            return PapyrusValue.None;
        });
        var emitter = host.Attach("Emitter", owner: PapyrusOwnerId.ForForm(SenderKey));
        Assert.True(host.Initialize(emitter).Completed, emitter.Failure);
        return new Rig(host, registry, emitter, heard);
    }

    private static ScriptInstance Listen(Rig rig, int id)
    {
        var listener = rig.Host.Attach("Listener", new Dictionary<string, PapyrusValue>
        {
            ["Sender"] = PapyrusValue.FromObject(new EngineObjectReference(SenderKey)),
            ["Id"] = PapyrusValue.FromInt(id),
        });
        Assert.True(rig.Host.Initialize(listener).Completed, listener.Failure);
        return listener;
    }

    [Fact]
    public void RegisteringAddsTheReceiverAndUnregisteringRemovesIt()
    {
        var rig = Setup();
        var listener = Listen(rig, 1);

        Assert.Same(listener, Assert.Single(rig.Registry.ReceiversOf(SenderKey, "emitter_tick")));
        Assert.True(rig.Host.RunEvent(listener, "Quit").Completed);
        Assert.Empty(rig.Registry.ReceiversOf(SenderKey, "emitter_Tick"));
    }

    [Fact]
    public void ASenderIsTheSameFromAFormOwnerAndFromABareIdentity()
    {
        var owner = PapyrusOwnerId.ForForm(SenderKey);

        Assert.Equal(CustomEventNativeAdapters.SenderKey(PapyrusValue.FromObject(new EngineObjectReference(owner))),
            CustomEventNativeAdapters.SenderKey(PapyrusValue.FromObject(new EngineObjectReference(SenderKey))));
    }

    [Fact]
    public void ASentEventIsQueuedInsideTheNativeAndReachesEveryReceiverOnceWithItsArgs()
    {
        var rig = Setup();
        Listen(rig, 1);
        Listen(rig, 2);

        Assert.True(rig.Host.RunEvent(rig.Emitter, "Fire", PapyrusValue.FromInt(7)).Completed);
        Assert.Empty(rig.Heard);
        Assert.Equal(1, rig.Registry.Pending);

        Assert.Equal(2, rig.Registry.Drain(rig.Host));
        Assert.Equal(new[] { (1L, 7L), (2L, 7L) }, rig.Heard);
        Assert.Equal(0, rig.Registry.Pending);
        Assert.Equal(2, rig.Registry.Delivered);

        Assert.Equal(0, rig.Registry.Drain(rig.Host));
        Assert.Equal(2, rig.Heard.Count);
    }

    [Fact]
    public void ASendWithoutAnArgumentArrayStillDelivers()
    {
        var rig = Setup();
        Listen(rig, 1);

        Assert.True(rig.Host.RunEvent(rig.Emitter, "FireBare").Completed);
        Assert.Equal(1, rig.Registry.Drain(rig.Host));
        Assert.Equal(new[] { (1L, -1L) }, rig.Heard);
    }

    [Fact]
    public void AnUnregisteredReceiverGetsNothingEvenWhenItLeftAfterTheSend()
    {
        var rig = Setup();
        var leaver = Listen(rig, 1);
        Listen(rig, 2);

        Assert.True(rig.Host.RunEvent(rig.Emitter, "Fire", PapyrusValue.FromInt(3)).Completed);
        Assert.True(rig.Host.RunEvent(leaver, "Quit").Completed);
        Assert.Equal(1, rig.Registry.Drain(rig.Host));
        Assert.Equal(new[] { (2L, 3L) }, rig.Heard);
    }

    [Fact]
    public void ASendWithNoReceiversIsCountedAndDoesNotThrow()
    {
        var rig = Setup();

        Assert.True(rig.Host.RunEvent(rig.Emitter, "Fire", PapyrusValue.FromInt(1)).Completed);
        Assert.Equal(0, rig.Registry.Drain(rig.Host));
        Assert.Equal(1, rig.Registry.NoReceivers);
        Assert.Empty(rig.Heard);
    }

    [Fact]
    public void ADetachedReceiverIsSkippedAndTheOthersStillHearTheEvent()
    {
        var rig = Setup();
        var gone = Listen(rig, 1);
        Listen(rig, 2);
        Assert.True(rig.Host.RunEvent(rig.Emitter, "Fire", PapyrusValue.FromInt(5)).Completed);

        gone.Detached = true;

        Assert.Equal(1, rig.Registry.Drain(rig.Host));
        Assert.Equal(new[] { (2L, 5L) }, rig.Heard);
    }

    [Fact]
    public void AReceiverThatAReattachedCellBringsBackKeepsItsRegistration()
    {
        var rig = Setup();
        var parked = Listen(rig, 1);

        parked.Detached = true;
        Assert.True(rig.Host.RunEvent(rig.Emitter, "Fire", PapyrusValue.FromInt(1)).Completed);
        Assert.Equal(0, rig.Registry.Drain(rig.Host));
        parked.Detached = false;
        Assert.True(rig.Host.RunEvent(rig.Emitter, "Fire", PapyrusValue.FromInt(2)).Completed);

        Assert.Equal(1, rig.Registry.Drain(rig.Host));
        Assert.Equal(new[] { (1L, 2L) }, rig.Heard);
    }

    [Fact]
    public void ADiscardedReceiverIsPrunedAtTheNextDrain()
    {
        var rig = Setup();
        var discarded = Listen(rig, 1);
        rig.Host.Discard(discarded);
        Assert.True(rig.Host.RunEvent(rig.Emitter, "Fire", PapyrusValue.FromInt(1)).Completed);

        Assert.Equal(0, rig.Registry.Drain(rig.Host));

        Assert.Empty(rig.Registry.ReceiversOf(SenderKey, "emitter_Tick"));
        discarded.Detached = false;
        Assert.Empty(rig.Registry.ReceiversOf(SenderKey, "emitter_Tick"));
    }

    [Fact]
    public void AReceiverThatDoesNotDeclareTheHandlerIsCountedAsFailedNotThrown()
    {
        var rig = Setup();
        var stranger = rig.Host.Attach("Emitter", owner: PapyrusOwnerId.ForForm("Q7:Test.esm"));
        Assert.True(rig.Host.Initialize(stranger).Completed);
        rig.Registry.Register(stranger, SenderKey, "emitter_Tick");

        Assert.True(rig.Host.RunEvent(rig.Emitter, "Fire", PapyrusValue.FromInt(1)).Completed);
        Assert.Equal(1, rig.Registry.Drain(rig.Host));
        Assert.Equal(1, rig.Registry.Failed);
    }

    [Fact]
    public void ClearDropsTheQueuedSends()
    {
        var rig = Setup();
        Listen(rig, 1);
        Assert.True(rig.Host.RunEvent(rig.Emitter, "Fire", PapyrusValue.FromInt(1)).Completed);

        rig.Registry.Clear();

        Assert.Equal(0, rig.Registry.Drain(rig.Host));
        Assert.Empty(rig.Heard);
    }

    [Fact]
    public void ForgetAllDropsEveryReceiver()
    {
        var rig = Setup();
        Listen(rig, 1);

        rig.Registry.ForgetAll();

        Assert.Empty(rig.Registry.ReceiversOf(SenderKey, "emitter_Tick"));
    }

    [Fact]
    public void AliasesAndDynamicReferencesOfOneBaseFormAreDistinctSenders()
    {
        static string Key(PapyrusOwnerId owner) => CustomEventNativeAdapters.SenderKey(PapyrusValue.FromObject(new EngineObjectReference(owner)));

        Assert.NotEqual(Key(PapyrusOwnerId.ForQuestAlias("Q9:Test.esm", 1)), Key(PapyrusOwnerId.ForQuestAlias("Q9:Test.esm", 2)));
        Assert.NotEqual(Key(PapyrusOwnerId.ForDynamicReference("B:Test.esm", "a")), Key(PapyrusOwnerId.ForDynamicReference("B:Test.esm", "b")));
        Assert.NotEqual(Key(PapyrusOwnerId.ForQuestAlias("Q9:Test.esm", 1)), Key(PapyrusOwnerId.ForForm("Q9:Test.esm")));
        Assert.Equal("R:Test.esm", Key(PapyrusOwnerId.ForPlacedReference("B:Test.esm", "R:Test.esm")));
        Assert.Equal("Q9:Test.esm", Key(PapyrusOwnerId.ForForm("Q9:Test.esm")));
    }

    [Fact]
    public void AFullQueueRefusesTheSendByNameInsteadOfDroppingIt()
    {
        var rig = Setup();
        Listen(rig, 1);
        for (var i = 0; i < CustomEventRegistry.Capacity; ++i)
            Assert.True(rig.Registry.Send(PapyrusValue.FromObject(rig.Emitter), "emitter_Tick", PapyrusValue.None));

        var result = rig.Host.RunEvent(rig.Emitter, "Fire", PapyrusValue.FromInt(1));

        Assert.False(result.Completed, result.Message);
        Assert.Contains("emitter_Tick", result.Message + rig.Host.LastNativeFailure, StringComparison.Ordinal);
        Assert.Equal(CustomEventRegistry.Capacity, rig.Registry.Pending);
    }
}
