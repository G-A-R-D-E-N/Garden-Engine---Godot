using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReferenceActivateNativeAdaptersTests : HostFixture
{
    private const string Activator = "000014:Base.esm";
    private static readonly PapyrusOwnerId Target = PapyrusOwnerId.ForPlacedReference("000800:Base.esm", "000A00");

    private sealed record Rig(IScriptHost Host, PapyrusEventBridge Bridge, DeferredActivations Queue, List<string> Seen, List<bool> Ran, ScriptInstance Caller);

    private static EvidenceGate Gate(string status = "APPROXIMATED") => TestHosts.Gate((ReferenceActivateNativeAdapters.SystemKey, status));

    private Rig Setup(HostKind kind, EvidenceGate gate, string body, bool registerTarget = true, Action<DeferredActivations>? onActivate = null)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("ObjectReference", null, "bool Function Activate(ObjectReference akActivator, bool abDefaultProcessingOnly = false) native"),
            TestHosts.Stub("Probe", null,
                "ObjectReference Function Target() global native",
                "ObjectReference Function Who() global native",
                "Function Seen(ObjectReference value) global native",
                "Function Ran(bool value) global native",
                "Function Again() global native"),
            new ScriptSpec("Door",
                "Scriptname Door extends ObjectReference\nEvent OnActivate(ObjectReference akActionRef)\n  Probe.Seen(akActionRef)\n  Probe.Again()\nEndEvent\n"),
            new ScriptSpec("Caller", $"Scriptname Caller\nFunction Run()\n  ObjectReference target = Probe.Target()\n  ObjectReference who = Probe.Who()\n  {body}\nEndFunction\n")));
        var bridge = new PapyrusEventBridge(host, gate);
        var queue = new DeferredActivations(bridge);
        var seen = new List<string>();
        var ran = new List<bool>();
        host.RegisterNative(new NativeId("Probe", "Target"), _ => PapyrusValue.FromObject(new EngineObjectReference(Target)));
        host.RegisterNative(new NativeId("Probe", "Who"), _ => PapyrusValue.FromObject(new EngineObjectReference(Activator)));
        host.RegisterNative(new NativeId("Probe", "Seen"), arguments =>
        {
            seen.Add(arguments[0].Reference is EngineObjectReference reference ? reference.Identity : "None");
            return PapyrusValue.None;
        });
        host.RegisterNative(new NativeId("Probe", "Ran"), arguments =>
        {
            ran.Add(arguments[0].Flag);
            return PapyrusValue.None;
        });
        host.RegisterNative(new NativeId("Probe", "Again"), _ =>
        {
            onActivate?.Invoke(queue);
            return PapyrusValue.None;
        });
        var door = host.Attach("Door", null, Target);
        Assert.True(host.Initialize(door).Completed);
        if (registerTarget) bridge.Register(Target, door);
        var caller = host.Attach("Caller");
        Assert.True(host.Initialize(caller).Completed);
        return new Rig(host, bridge, queue, seen, ran, caller);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ActivateQueuesOnActivateAndDeliversItOnlyWhenTheQueueIsDrained(HostKind kind)
    {
        var gate = Gate();
        var rig = Setup(kind, gate, "Probe.Ran(target.Activate(who))");
        var report = new ReferenceActivateNativeAdapters(rig.Host, gate, rig.Queue).Bind();

        var result = rig.Host.RunEvent(rig.Caller, "Run");

        Assert.Equal(1, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(rig.Host, result));
        Assert.Equal(new[] { false }, rig.Ran.ToArray());
        Assert.Equal(1, rig.Queue.Pending);
        Assert.Empty(rig.Seen);

        Assert.Equal(1, rig.Queue.Drain());
        Assert.Equal(new[] { Activator }, rig.Seen.ToArray());
        Assert.Equal(0, rig.Queue.Pending);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void DefaultProcessingOnlySendsNoEventButRunsDefaultProcessing(HostKind kind)
    {
        var gate = Gate();
        var rig = Setup(kind, gate, "Probe.Ran(target.Activate(who, True))");
        new ReferenceActivateNativeAdapters(rig.Host, gate, rig.Queue).Bind();

        var result = rig.Host.RunEvent(rig.Caller, "Run");

        Assert.True(result.Completed, TestHosts.Describe(rig.Host, result));
        Assert.Equal(new[] { false }, rig.Ran.ToArray());
        Assert.Equal(1, rig.Queue.Pending);
        var defaults = new List<string>();
        rig.Queue.DefaultProcessing = (key, _) =>
        {
            defaults.Add(key);
            return true;
        };

        Assert.Equal(0, rig.Queue.Drain());

        Assert.Empty(rig.Seen);
        Assert.Single(defaults);
        Assert.Equal(1, rig.Queue.DefaultRun);
        Assert.Equal(0, rig.Queue.Pending);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void DefaultProcessingRunsAfterOnActivateEvenWithoutAScriptOnTheTarget(HostKind kind)
    {
        var gate = Gate();
        var rig = Setup(kind, gate, "target.Activate(who)", registerTarget: false);
        new ReferenceActivateNativeAdapters(rig.Host, gate, rig.Queue).Bind();
        Assert.True(rig.Host.RunEvent(rig.Caller, "Run").Completed);
        var defaults = 0;
        rig.Queue.DefaultProcessing = (_, _) => ++defaults > 0;

        Assert.Equal(0, rig.Queue.Drain());

        Assert.Equal(1, defaults);
        Assert.Equal(1, rig.Queue.Unrouted);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ADrainSendsOnlyTheRequestsQueuedBeforeItStarted(HostKind kind)
    {
        var gate = Gate();
        var rig = Setup(kind, gate, "target.Activate(who)", onActivate: queue => queue.Enqueue("000A00", PapyrusValue.None));
        new ReferenceActivateNativeAdapters(rig.Host, gate, rig.Queue).Bind();
        Assert.True(rig.Host.RunEvent(rig.Caller, "Run").Completed);

        Assert.Equal(1, rig.Queue.Drain());
        Assert.Equal(1, rig.Queue.Pending);
        Assert.Equal(1, rig.Queue.Drain());
        Assert.Equal(new[] { Activator, "None" }, rig.Seen.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ATargetWithNoRegisteredScriptIsCountedAndNothingIsSent(HostKind kind)
    {
        var gate = Gate();
        var rig = Setup(kind, gate, "target.Activate(who)", registerTarget: false);
        new ReferenceActivateNativeAdapters(rig.Host, gate, rig.Queue).Bind();
        Assert.True(rig.Host.RunEvent(rig.Caller, "Run").Completed);

        Assert.Equal(0, rig.Queue.Drain());

        Assert.Equal(1, rig.Queue.Unrouted);
        Assert.Empty(rig.Seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheQueueIsBoundedAndClearDropsWhatIsPending(HostKind kind)
    {
        var gate = Gate();
        var rig = Setup(kind, gate, "target.Activate(who)");

        for (var index = 0; index < DeferredActivations.Capacity; index++) Assert.True(rig.Queue.Enqueue("000A00", PapyrusValue.None));
        Assert.False(rig.Queue.Enqueue("000A00", PapyrusValue.None));

        Assert.Equal(DeferredActivations.Capacity, rig.Queue.Pending);
        Assert.Equal(1, rig.Queue.Dropped);
        rig.Queue.Clear();
        Assert.Equal(0, rig.Queue.Pending);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TryFindReferenceAnswersOnlyAPlacedOrDynamicReferenceWithARegisteredScript(HostKind kind)
    {
        var gate = Gate();
        var rig = Setup(kind, gate, "target.Activate(who)");
        var dynamic = PapyrusOwnerId.ForDynamicReference("000801:Base.esm", "Dynamic:0000000A");
        var form = PapyrusOwnerId.ForForm("000802:Base.esm");
        var other = rig.Host.Attach("Door", null, dynamic);
        rig.Bridge.Register(dynamic, other);
        rig.Bridge.Register(form, rig.Host.Attach("Door"));

        Assert.True(rig.Bridge.TryFindReference("000A00", out var placed));
        Assert.Equal(Target, placed);
        Assert.True(rig.Bridge.TryFindReference("Dynamic:0000000A", out var found));
        Assert.Equal(dynamic, found);
        Assert.False(rig.Bridge.TryFindReference("000999", out _));
        Assert.False(rig.Bridge.TryFindReference("", out _));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANoneActivatorIsQueuedAndDeliveredAsNone(HostKind kind)
    {
        var gate = Gate();
        var rig = Setup(kind, gate, "target.Activate(None)");
        new ReferenceActivateNativeAdapters(rig.Host, gate, rig.Queue).Bind();

        Assert.True(rig.Host.RunEvent(rig.Caller, "Run").Completed);

        Assert.Equal(1, rig.Queue.Drain());
        Assert.Equal(new[] { "None" }, rig.Seen.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsTheNative(HostKind kind)
    {
        var gate = Gate("UNPROVEN");
        var rig = Setup(kind, gate, "target.Activate(who)");

        var report = new ReferenceActivateNativeAdapters(rig.Host, gate, rig.Queue).Bind();

        Assert.Equal(1, report.Bound);
        Assert.All(report.Natives, outcome => Assert.Equal(AdapterState.Bound, outcome.State));
    }
}
