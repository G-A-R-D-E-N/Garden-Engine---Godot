using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReferenceNativeSelfTests : HostFixture
{
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "First:Probe.esp");
    private static readonly PapyrusOwnerId Activator = PapyrusOwnerId.ForPlacedReference("Actor:Probe.esp", "Player:Probe.esp");
    private const string Metadata = "[{\"name\":\"ReferenceProbe\",\"properties\":[]}]";

    private static readonly string[] Keys =
    {
        "vm.session.cell_lifecycle",
        "vm.session.activation_routing",
        "vmad.reference_compiled_attachments",
        "vmad.reference_source",
        "owner.engine_identity",
        "event.OnActivate",
        NativeWait.Key,
    };

    private static readonly ScriptSpec FormStub = new("Form", "Scriptname Form\n");

    private static readonly ScriptSpec ObjectReferenceStub = new("ObjectReference",
        "Scriptname ObjectReference\n" +
        "Function Observe(Int value) native\n" +
        "Int Function Wait() native\n");

    private static readonly ScriptSpec ExternalStub = new("External",
        "Scriptname External\n" +
        "Function Observe() global native\n");

    private static EvidenceGate Gate(string? denied = null) => denied is null
        ? TestHosts.Gate(Keys.Select(key => (key, "ENGINE_POLICY")).ToArray())
        : TestHosts.GateDenying(denied, Keys);

    private static ScriptSpec Probe(bool onInit, params string[] body) => new("ReferenceProbe",
        "Scriptname ReferenceProbe extends ObjectReference\n" +
        (onInit ? "Function OnInit()\n" : "Function OnActivate(ObjectReference akActionRef)\n") +
        string.Concat(body.Select(line => "  " + line + "\n")) +
        "EndFunction\n");

    private static ScriptSpec[] Specs(ScriptSpec probe) =>
        new[] { FormStub, ObjectReferenceStub, ExternalStub, probe };

    private GameSession Session(HostKind kind, ScriptSpec probe, EvidenceGate? gate = null)
    {
        var evidence = gate ?? Gate();
        var host = Track(TestHosts.Create(kind, evidence, Specs(probe)));
        return new GameSession(host, evidence);
    }

    private static void AssertStopped(IScriptHost host, VmResult result)
    {
        Assert.False(result.Completed, TestHosts.Describe(host, result));
    }

    public static IEnumerable<object[]> HostAndBool()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            yield return new object[] { host[0], false };
            yield return new object[] { host[0], true };
        }
    }

    [Theory]
    [MemberData(nameof(HostAndBool))]
    public void AnAttachedReferenceSuppliesItsExplicitOwnerToEngineNatives(HostKind kind, bool onInit)
    {
        var session = Session(kind, Probe(onInit, "Observe(7)"));
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
        Assert.Equal(0, TestHosts.NativeTargetRefusals(session.Host));
    }

    [Theory]
    [MemberData(nameof(HostAndBool))]
    public void SelfReceiverSurvivesLatentCompletionAndExactInstanceReattachment(HostKind kind, bool completeFirst)
    {
        var session = Session(kind, Probe(false, "Int got = Wait()", "Observe(got)"));
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
        session.ReattachCell("Cell");
        Assert.Same(instance, Assert.Single(session.AttachReference("Cell", Owner, Metadata).Instances));
        session.Host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(9), Assert.Single(observed));
        Assert.Equal(1, session.Events.Pending);
        Assert.Equal(0, session.Events.Delivered);
        Assert.Equal(0, session.Host.WaitingCount);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ScriptInstanceNativesStillReceiveTheOriginalScriptSelf(HostKind kind)
    {
        var session = Session(kind, Probe(false, "Observe(7)"));
        ScriptInstance? observed = null;
        session.Host.RegisterNative(new NativeId("ObjectReference", "Observe"), (self, _) =>
        {
            observed = Assert.IsType<ScriptInstance>(self.Reference);
            return PapyrusValue.None;
        }, NativeTargetKind.ScriptInstance);
        var instance = Assert.Single(session.AttachReference("Cell", Owner, Metadata).Instances);
        Assert.Equal(1, session.Activate(Owner, Activator).Delivered);
        Assert.Same(instance, observed);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void StaticNativesReceiveNoScriptSelf(HostKind kind)
    {
        var session = Session(kind, Probe(false, "External.Observe()"));
        PapyrusValue? observed = null;
        session.Host.RegisterNative(new NativeId("External", "Observe"), (self, _) =>
        {
            observed = self;
            return PapyrusValue.None;
        }, NativeTargetKind.Static);
        Assert.Single(session.AttachReference("Cell", Owner, Metadata).Instances);
        Assert.Equal(1, session.Activate(Owner, Activator).Delivered);
        Assert.Equal(PapyrusValue.None, observed);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AStandaloneScriptCannotFabricateAnEngineReceiver(HostKind kind)
    {
        var session = Session(kind, Probe(false, "Observe(7)"));
        var calls = 0;
        session.Host.RegisterNative(new NativeId("ObjectReference", "Observe"), (_, _) =>
        {
            calls++;
            return PapyrusValue.None;
        }, NativeTargetKind.EngineObject);
        var instance = session.Host.Attach("ReferenceProbe");
        Assert.True(session.Host.Initialize(instance).Completed);
        var result = session.Host.RunEvent(instance, "OnActivate", PapyrusValue.None);
        AssertStopped(session.Host, result);
        Assert.Equal(0, calls);
        Assert.Equal(1, TestHosts.NativeTargetRefusals(session.Host));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnInvalidOwnerCannotLeaveAnAttachedInstanceBehind(HostKind kind)
    {
        var session = Session(kind, Probe(false, "Observe(7)"));
        Assert.Throws<ArgumentException>(() => session.Host.Attach("ReferenceProbe", owner: default(PapyrusOwnerId)));
        Assert.Empty(session.Host.Instances);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ACellBindingCannotClaimAnotherOwnersReferenceKey(HostKind kind)
    {
        var session = Session(kind, Probe(false, "Observe(7)"));
        Assert.Throws<ArgumentException>(() => session.Cells.Bind("Cell", "another-reference", "ReferenceProbe", owner: Owner));
        Assert.Empty(session.Host.Instances);
        Assert.Equal(0, session.Cells.BoundInstances);
    }

}
