using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class LatentNativeTests : HostFixture
{
    private static readonly NativeId Answer = new("External", "Answer");
    private static readonly NativeId MethodAnswer = new("Mailbox", "Answer");
    private static readonly NativeId Observe = new("External", "Observe");

    private static readonly ScriptSpec ExternalSpec = new("External",
        "Scriptname External\n" +
        "Int Function Answer() global native\n" +
        "Function Observe(Int value) global native\n" +
        "Mailbox Function Target() global native\n");

    private static readonly ScriptSpec MailboxSpec = new("Mailbox",
        "Scriptname Mailbox\n" +
        "Int Function Answer() native\n");

    private static ScriptSpec CallerSpec(bool method) => new("Caller",
        "Scriptname Caller\n" +
        "Function Run()\n" +
        (method ? "  Mailbox target = External.Target()\n  Int got = target.Answer()\n" : "  Int got = External.Answer()\n") +
        "  External.Observe(got)\n" +
        "EndFunction\n" +
        "Function Quick()\n" +
        "EndFunction\n" +
        "Function OnQuick()\n" +
        "  External.Observe(99)\n" +
        "EndFunction\n");

    private static readonly ScriptSpec NestedSpec = new("Nested",
        "Scriptname Nested\n" +
        "Function Run()\n" +
        "  Int got = Inner()\n" +
        "  External.Observe(got)\n" +
        "EndFunction\n" +
        "Int Function Inner()\n" +
        "  Int got = External.Answer()\n" +
        "  Return got\n" +
        "EndFunction\n");

    private static EvidenceGate Gate(bool allowWait = true) => allowWait
        ? TestHosts.Gate((PapyrusEventBridge.KeyPrefix + "OnQuick", "ENGINE_POLICY"))
        : TestHosts.GateDenying(NativeWait.Key, PapyrusEventBridge.KeyPrefix + "OnQuick");

    public static IEnumerable<object[]> HostAndBool()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            yield return new object[] { host[0], false };
            yield return new object[] { host[0], true };
        }
    }

    public static IEnumerable<object[]> HostAndTwoBools()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var first in new[] { false, true })
            {
                yield return new object[] { host[0], first, false };
                yield return new object[] { host[0], first, true };
            }
        }
    }

    private IScriptHost Host(HostKind kind, out List<PapyrusValue> observed, Func<long>? token = null,
        bool allowWait = true, bool method = false)
    {
        var host = Track(TestHosts.Create(kind, Gate(allowWait), ExternalSpec, MailboxSpec, CallerSpec(method), NestedSpec));
        host.RegisterNative(method ? MethodAnswer : Answer, _ => throw new NativeWait(token?.Invoke() ?? 1),
            method ? NativeTargetKind.EngineObject : NativeTargetKind.Static);
        host.RegisterNative(new NativeId("External", "Target"),
            _ => PapyrusValue.FromObject(new EngineObjectReference("mailbox")));
        var values = new List<PapyrusValue>();
        host.RegisterNative(Observe, args => { values.Add(args[0]); return PapyrusValue.None; });
        observed = values;
        return host;
    }

    private static ScriptInstance Attach(IScriptHost host, string script = "Caller")
    {
        var instance = host.Attach(script);
        Assert.True(host.Initialize(instance).Completed);
        return instance;
    }

    [Theory]
    [MemberData(nameof(HostAndBool))]
    public void ADirectEventWaitsAndResumesPastTheNativeCall(HostKind kind, bool method)
    {
        var host = Host(kind, out var observed, method: method);
        var result = host.RunEvent(Attach(host), "Run");
        Assert.Equal(VmStopReason.Waiting, result.Reason);
        Assert.Empty(observed);
        Assert.Equal(1, host.WaitingCount);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(7)));
        host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(7), Assert.Single(observed));
        Assert.Equal(0, host.WaitingCount);
        Assert.False(host.CompleteLatent(1, PapyrusValue.FromInt(9)));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void WaitingSurvivesMoreThanEightStepsWithoutSpendingOrParking(HostKind kind)
    {
        var host = Host(kind, out var observed);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(Attach(host), "Run").Reason);
        for (var index = 0; index < 12; index++)
        {
            Assert.Equal(0, host.RunStep(16));
            Assert.Equal(1, host.WaitingCount);
        }
        Assert.Equal((0, 0, 1, 0), TestHosts.Counters(host));
        Assert.Empty(observed);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(42)));
        Assert.Equal(1, host.RunStep(16));
        Assert.Equal(PapyrusValue.FromInt(42), Assert.Single(observed));
        Assert.Equal(0, host.WaitingCount);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void UnknownAndDuplicateCompletionsAreRefusedAndCounted(HostKind kind)
    {
        var host = Host(kind, out var observed);
        host.RunEvent(Attach(host), "Run");
        Assert.False(host.CompleteLatent(99, PapyrusValue.None));
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(3)));
        Assert.False(host.CompleteLatent(1, PapyrusValue.FromInt(4)));
        host.RunStep(32);
        Assert.False(host.CompleteLatent(1, PapyrusValue.None));
        Assert.Equal(3, TestHosts.Counters(host).RefusedCompletions);
        Assert.Equal(PapyrusValue.FromInt(3), Assert.Single(observed));
    }

    [Theory]
    [MemberData(nameof(HostAndBool))]
    public void ADetachedCompletionIsHeldUntilTheSameInstanceReattaches(HostKind kind, bool completeBeforeDetach)
    {
        var host = Host(kind, out var observed);
        var session = new CellScriptSession(host);
        var instance = session.Bind("cell", "reference", "Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.Waiting, session.Activate("reference", "Run").Result!.Reason);
        if (completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        Assert.Same(instance, Assert.Single(session.DetachCell("cell")));
        if (!completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        Assert.False(host.CompleteLatent(1, PapyrusValue.None));
        Assert.Equal(1, session.Tick(32));
        Assert.Equal(PapyrusValue.FromInt(5), Assert.Single(observed));
        Assert.Same(instance, Assert.Single(session.ReattachCell("cell")));
        session.Tick(32);
        Assert.Equal(PapyrusValue.FromInt(5), Assert.Single(observed));
        Assert.Equal(0, host.WaitingCount);
    }

    [Theory]
    [MemberData(nameof(HostAndTwoBools))]
    public void DirectEventsRefuseDetachedInstancesWithoutLosingLatentAnswers(HostKind kind, bool completeBeforeDetach,
        bool useOverride)
    {
        var host = Host(kind, out var observed);
        if (useOverride)
        {
            host.Overrides = new ScriptOverrideRegistry(host.Gate);
            host.Overrides.Register("Caller", "OnQuick", 0, _ =>
            {
                observed.Add(PapyrusValue.FromInt(99));
                return OverrideResult.Handled(PapyrusValue.None);
            });
        }
        var session = new CellScriptSession(host);
        var instance = session.Bind("cell", "reference", "Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        if (completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        session.DetachCell("cell");

        foreach (var function in new[] { "OnQuick", "Run" })
        {
            var result = host.RunEvent(instance, function);
            Assert.Equal(VmStopReason.ModelError, result.Reason);
            Assert.Contains("detached", result.Message!);
            Assert.Equal(0, result.Instructions);
        }
        Assert.Equal(2, TestHosts.RefusedEvents(host));
        Assert.Equal(0, host.Overrides?.HandledCount ?? 0);
        if (!completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        Assert.Equal(1, session.Tick(32));
        Assert.Equal(0, host.WaitingCount);
        Assert.Equal((0, 0, 1, 0), TestHosts.Counters(host));

        Assert.Same(instance, Assert.Single(session.ReattachCell("cell")));
        Assert.Equal(PapyrusValue.FromInt(5), Assert.Single(observed));
        Assert.Equal(0, host.WaitingCount);
        Assert.True(host.RunEvent(instance, "OnQuick").Completed);
        Assert.Equal(new[] { PapyrusValue.FromInt(5), PapyrusValue.FromInt(99) }, observed);
        Assert.Equal(2, TestHosts.RefusedEvents(host));
    }

    [Theory]
    [MemberData(nameof(HostAndTwoBools))]
    public void BridgeSkipsDetachedRecipientsAndRetainsTheirLatentAnswers(HostKind kind, bool completeBeforeDetach,
        bool broadcast)
    {
        var host = Host(kind, out var observed);
        var session = new CellScriptSession(host);
        var detached = session.Bind("cell", "reference", "Caller");
        Assert.True(host.Initialize(detached).Completed);
        var active = Attach(host);
        var owner = PapyrusOwnerId.ForPlacedReference("Base", "a");
        var other = PapyrusOwnerId.ForPlacedReference("Base", "b");
        var bridge = new PapyrusEventBridge(host, host.Gate);
        bridge.Register(owner, detached);
        bridge.Register(other, active);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(detached, "Run").Reason);
        if (completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        session.DetachCell("cell");

        var batch = bridge.Send(new EngineEvent("OnQuick", Array.Empty<PapyrusValue>(), broadcast ? null : owner));

        Assert.Equal(EventDelivery.SkippedParked, batch.Outcomes[0].Result);
        Assert.Equal(1, batch.SkippedParked);
        Assert.Equal(broadcast ? 1 : 0, batch.Delivered);
        Assert.Equal(0, batch.Refused);
        Assert.Equal(0, batch.Pending);
        Assert.Equal(0, TestHosts.RefusedEvents(host));
        Assert.Equal(broadcast ? new[] { PapyrusValue.FromInt(99) } : Array.Empty<PapyrusValue>(), observed);
        if (broadcast) Assert.Equal(EventDelivery.Delivered, batch.Outcomes[1].Result);
        if (!completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        session.Tick(32);
        Assert.Equal(0, host.WaitingCount);
        var counters = TestHosts.Counters(host);
        Assert.Equal((0, 1, 0), (counters.Parked, counters.WaitingEvents, counters.RefusedCompletions));

        Assert.Same(detached, Assert.Single(session.ReattachCell("cell")));
        Assert.Equal(PapyrusValue.FromInt(5), observed[^1]);
        Assert.Equal(broadcast ? 2 : 1, observed.Count);
        Assert.Equal(0, host.WaitingCount);
        Assert.Equal(broadcast ? 1 : 0, bridge.Delivered);
        Assert.Equal(1, bridge.SkippedParked);
        Assert.Equal(0, bridge.Pending);
        Assert.Equal(0, bridge.Refused);
        var attachedBatch = bridge.Send(new EngineEvent("OnQuick", Array.Empty<PapyrusValue>(), owner));
        Assert.Equal(1, attachedBatch.Delivered);
        Assert.Equal(0, attachedBatch.SkippedParked);
        Assert.Equal(broadcast ? 2 : 1, bridge.Delivered);
        Assert.Equal(1, bridge.SkippedParked);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void WaitingDoesNotStarveReadyEvents(HostKind kind)
    {
        var host = Host(kind, out var observed);
        var instance = Attach(host);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        var quick = host.RunEvent(instance, "Quick");
        Assert.True(quick.Completed, TestHosts.Describe(host, quick));
        Assert.Equal(1, host.WaitingCount);
        Assert.Empty(observed);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AReusedTokenCannotAcquireASecondWaitingEvent(HostKind kind)
    {
        var host = Host(kind, out var observed);
        var instance = Attach(host);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(1)));
        host.RunStep(32);
        var reused = host.RunEvent(instance, "Run");
        Assert.Equal(VmStopReason.ModelError, reused.Reason);
        Assert.Equal(0, host.WaitingCount);
        Assert.Single(observed);
    }


    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void CompletionTargetsOnlyItsOwnWaitingFrame(HostKind kind)
    {
        long next = 0;
        var host = Host(kind, out var observed, () => ++next);
        host.RunEvent(Attach(host), "Run");
        host.RunEvent(Attach(host), "Run");
        Assert.Equal(2, host.WaitingCount);
        Assert.True(host.CompleteLatent(2, PapyrusValue.FromInt(22)));
        host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(22), Assert.Single(observed));
        Assert.Equal(1, host.WaitingCount);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(11)));
        host.RunStep(32);
        Assert.Equal(new[] { PapyrusValue.FromInt(22), PapyrusValue.FromInt(11) }, observed);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AWaitInsideAScriptCallRetainsTheCallerAndReturnDestination(HostKind kind)
    {
        var host = Host(kind, out var observed);
        var instance = Attach(host, "Nested");
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(8)));
        host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(8), Assert.Single(observed));
    }

    private static readonly ScriptSpec Starter = new("Starter",
        "Scriptname Starter\n" +
        "Int Property Seen Auto\n" +
        "Int Property After Auto\n" +
        "Event OnInit()\n" +
        "  Int got = External.Answer()\n" +
        "  Seen = got\n" +
        "  External.Observe(got)\n" +
        "EndEvent\n" +
        "Function Poke()\n" +
        "  After = 7\n" +
        "EndFunction\n");

    private RadicleScriptHost StarterHost(out List<PapyrusValue> observed)
    {
        var seen = new List<PapyrusValue>();
        observed = seen;
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, Gate(), ExternalSpec, MailboxSpec, Starter));
        host.RegisterNative(Answer, _ => throw new NativeWait(5), NativeTargetKind.Static);
        host.RegisterNative(Observe, arguments => { seen.Add(arguments[0]); return PapyrusValue.None; }, NativeTargetKind.Static);
        return host;
    }

    [Fact]
    public void AWaitBegunInOnInitResumesAndTheScriptIsNotReadyUntilItEnds()
    {
        var host = StarterHost(out var observed);
        var instance = host.Attach("Starter");

        var init = host.Initialize(instance);

        Assert.Equal(VmStopReason.Waiting, init.Reason);
        Assert.Equal(ScriptLifecycle.Initializing, instance.Lifecycle);
        Assert.Equal(1, host.WaitingCount);
        Assert.True(host.CompleteLatent(5, PapyrusValue.FromInt(9)));
        host.RunStep(64);

        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.Equal(PapyrusValue.FromInt(9), Assert.Single(observed));
        Assert.True(host.TryReadScalarProperty(instance, "Seen", out var seen));
        Assert.Equal(9, Convert.ToInt32(seen));
        Assert.False(host.CompleteLatent(5, PapyrusValue.FromInt(1)));
    }

    [Fact]
    public void AnEventDuringAPendingOnInitIsHeldAndDeliveredOnceReady()
    {
        var host = StarterHost(out _);
        var instance = host.Attach("Starter");
        Assert.Equal(VmStopReason.Waiting, host.Initialize(instance).Reason);

        var held = host.RunEvent(instance, "Poke");

        Assert.Equal(VmStopReason.Waiting, held.Reason);
        Assert.Contains("held until", held.Message, StringComparison.Ordinal);
        Assert.False(host.TryReadScalarProperty(instance, "After", out var before) && Convert.ToInt32(before) == 7);
        Assert.True(host.CompleteLatent(5, PapyrusValue.FromInt(1)));
        host.RunStep(64);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.True(host.TryReadScalarProperty(instance, "After", out var after));
        Assert.Equal(7, Convert.ToInt32(after));
    }

    [Fact]
    public void AnOnInitThatFailsAfterItsWaitSurfacesTheFailureAndRefusesHeldEvents()
    {
        var host = StarterHost(out _);
        host.RegisterNative(Observe, _ => throw new InvalidOperationException("observer refused"), NativeTargetKind.Static);
        var instance = host.Attach("Starter");
        Assert.Equal(VmStopReason.Waiting, host.Initialize(instance).Reason);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Poke").Reason);

        Assert.True(host.CompleteLatent(5, PapyrusValue.FromInt(1)));
        host.RunStep(64);

        Assert.Equal(ScriptLifecycle.Failed, instance.Lifecycle);
        Assert.True(instance.Failure!.Contains("observer refused", StringComparison.Ordinal), instance.Failure);
        Assert.Equal(1, host.RefusedEvents);
        Assert.False(host.RunEvent(instance, "Poke").Completed);
    }

    [Fact]
    public void AFullHeldQueueRefusesTheNextEventByName()
    {
        var host = StarterHost(out _);
        var instance = host.Attach("Starter");
        Assert.Equal(VmStopReason.Waiting, host.Initialize(instance).Reason);
        for (var i = 0; i < RadicleScriptHost.MaxHeldEvents; ++i) Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Poke").Reason);

        var refused = host.RunEvent(instance, "Poke");

        Assert.False(refused.Completed);
        Assert.Contains("already held", refused.Message, StringComparison.Ordinal);
    }

}
