using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReferenceTimerNativeAdaptersTests : HostFixture
{
    private sealed record Rig(IScriptHost Host, ScriptTimers Timers, List<long> Fired, AdapterReport Report);

    private static EvidenceGate Gate(string status = "APPROXIMATED") => TestHosts.Gate((ReferenceTimerNativeAdapters.SystemKey, status));

    private Rig Setup(HostKind kind, string status = "APPROXIMATED")
    {
        var gate = Gate(status);
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("ScriptObject", null,
                "Function StartTimer(float afInterval, int aiTimerID = 0) native",
                "Function CancelTimer(int aiTimerID = 0) native"),
            TestHosts.Stub("ObjectReference", "ScriptObject"),
            TestHosts.Stub("Probe", null, "Function Fired(int value) global native"),
            new ScriptSpec("Timed",
                "Scriptname Timed extends ObjectReference\n" +
                "Function Run()\n  StartTimer(1.5, 7)\nEndFunction\n" +
                "Function RunDefault()\n  StartTimer(1.0)\nEndFunction\n" +
                "Function RunTwo()\n  StartTimer(2.0, 1)\n  StartTimer(1.0, 2)\nEndFunction\n" +
                "Function Repeat()\n  StartTimer(1.0, 9)\nEndFunction\n" +
                "Function Stop()\n  CancelTimer(7)\nEndFunction\n" +
                "Function RunPair()\n  StartTimer(1.0, 11)\n  StartTimer(1.0, 12)\nEndFunction\n" +
                "Event OnTimer(int aiTimerID)\n  Probe.Fired(aiTimerID)\n  If aiTimerID == 9\n    StartTimer(1.0, 9)\n  EndIf\n  If aiTimerID == 11\n    CancelTimer(12)\n  EndIf\nEndEvent\n"),
            new ScriptSpec("TimedRestart",
                "Scriptname TimedRestart extends ObjectReference\n" +
                "Function RunPair()\n  StartTimer(1.0, 11)\n  StartTimer(1.0, 12)\nEndFunction\n" +
                "Event OnTimer(int aiTimerID)\n  Probe.Fired(aiTimerID)\n  If aiTimerID == 11\n    StartTimer(1.0, 12)\n  EndIf\nEndEvent\n"),
            new ScriptSpec("TimedZero",
                "Scriptname TimedZero extends ObjectReference\n" +
                "Function Run()\n  StartTimer(1.0, 11)\nEndFunction\n" +
                "Event OnTimer(int aiTimerID)\n  Probe.Fired(aiTimerID)\n  StartTimer(0.0, 11)\nEndEvent\n"),
            new ScriptSpec("Plain", "Scriptname Plain extends ScriptObject\nFunction Run()\n  StartTimer(1.0)\nEndFunction\n")));
        var fired = new List<long>();
        host.RegisterNative(new NativeId("Probe", "Fired"), arguments =>
        {
            fired.Add(arguments[0].Integer);
            return PapyrusValue.None;
        });
        var timers = new ScriptTimers();
        var report = new ReferenceTimerNativeAdapters(host, gate, timers).Bind();
        return new Rig(host, timers, fired, report);
    }

    private static ScriptInstance Attach(IScriptHost host, string script)
    {
        var instance = host.Attach(script);
        Assert.True(host.Initialize(instance).Completed);
        return instance;
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ATimerFiresOnTimerOnceWithItsIdAfterTheInterval(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Timed");

        var result = rig.Host.RunEvent(instance, "Run");

        Assert.Equal(2, rig.Report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(rig.Host, result));
        Assert.Equal(1, rig.Timers.Count);
        Assert.Equal(0, rig.Timers.Deliver(rig.Host, 1.0));
        Assert.Empty(rig.Fired);
        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 0.5));
        Assert.Equal(new long[] { 7 }, rig.Fired.ToArray());
        Assert.Equal(0, rig.Timers.Deliver(rig.Host, 10));
        Assert.Equal(1, rig.Fired.Count);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheTimerIdDefaultsToZero(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Timed");

        Assert.True(rig.Host.RunEvent(instance, "RunDefault").Completed);

        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 1.0));
        Assert.Equal(new long[] { 0 }, rig.Fired.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void CancelTimerStopsAPendingTimer(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Timed");
        Assert.True(rig.Host.RunEvent(instance, "Run").Completed);

        var result = rig.Host.RunEvent(instance, "Stop");

        Assert.True(result.Completed, TestHosts.Describe(rig.Host, result));
        Assert.Equal(0, rig.Timers.Count);
        Assert.Equal(0, rig.Timers.Deliver(rig.Host, 10));
        Assert.Empty(rig.Fired);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void StartingARunningTimerAgainRestartsIt(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Timed");
        Assert.True(rig.Host.RunEvent(instance, "Run").Completed);
        Assert.Equal(0, rig.Timers.Deliver(rig.Host, 1.0));

        Assert.True(rig.Host.RunEvent(instance, "Run").Completed);

        Assert.Equal(1, rig.Timers.Count);
        Assert.Equal(0, rig.Timers.Deliver(rig.Host, 1.0));
        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 0.5));
        Assert.Equal(new long[] { 7 }, rig.Fired.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TimersWithDifferentIdsFireInDueOrder(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Timed");
        Assert.True(rig.Host.RunEvent(instance, "RunTwo").Completed);

        Assert.Equal(2, rig.Timers.Deliver(rig.Host, 3.0));

        Assert.Equal(new long[] { 2, 1 }, rig.Fired.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ACancelInsideAHandlerStopsAnotherTimerDueInTheSameFrame(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Timed");
        Assert.True(rig.Host.RunEvent(instance, "RunPair").Completed);

        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 1.0));

        Assert.Equal(new long[] { 11 }, rig.Fired.ToArray());
        Assert.Equal(0, rig.Timers.Count);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ARestartInsideAHandlerDoesNotFireTheReplacedTimerInTheSameFrame(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "TimedRestart");
        Assert.True(rig.Host.RunEvent(instance, "RunPair").Completed);

        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 1.0));
        Assert.Equal(new long[] { 11 }, rig.Fired.ToArray());

        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 1.0));
        Assert.Equal(new long[] { 11, 12 }, rig.Fired.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AZeroIntervalStartedByAHandlerFiresOncePerDeliver(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "TimedZero");
        Assert.True(rig.Host.RunEvent(instance, "Run").Completed);

        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 1.0));
        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 1.0));

        Assert.Equal(new long[] { 11, 11 }, rig.Fired.ToArray());
        Assert.Equal(1, rig.Timers.Count);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ATimerBelongsToTheScriptInstanceThatStartedIt(HostKind kind)
    {
        var rig = Setup(kind);
        var starter = Attach(rig.Host, "Timed");
        Attach(rig.Host, "Timed");
        Assert.True(rig.Host.RunEvent(starter, "Run").Completed);

        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 2.0));

        Assert.Equal(new long[] { 7 }, rig.Fired.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AHandlerThatStartsTheTimerAgainFiresAgainOnTheNextInterval(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Timed");
        Assert.True(rig.Host.RunEvent(instance, "Repeat").Completed);

        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 1.0));
        Assert.Equal(1, rig.Timers.Count);
        Assert.Equal(1, rig.Timers.Deliver(rig.Host, 1.0));

        Assert.Equal(new long[] { 9, 9 }, rig.Fired.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ADiscardedInstanceIsSkippedAndCounted(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Timed");
        Assert.True(rig.Host.RunEvent(instance, "Run").Completed);
        rig.Host.Discard(instance);

        Assert.Equal(0, rig.Timers.Deliver(rig.Host, 2.0));

        Assert.Equal(1, rig.Timers.Skipped);
        Assert.Empty(rig.Fired);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AScriptThatIsNotAnObjectReferenceStartsItsOwnTimer(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Plain");

        var result = rig.Host.RunEvent(instance, "Run");

        Assert.True(result.Completed, TestHosts.Describe(rig.Host, result));
        Assert.Equal(1, rig.Timers.Count);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheTableRefusesANonFiniteIntervalAndIsBounded(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Timed");

        Assert.False(rig.Timers.Start(instance, double.NaN, 1));
        Assert.False(rig.Timers.Start(instance, double.PositiveInfinity, 1));
        for (var id = 0; id < ScriptTimers.Capacity; id++) Assert.True(rig.Timers.Start(instance, 1, id));

        Assert.False(rig.Timers.Start(instance, 1, ScriptTimers.Capacity));
        Assert.True(rig.Timers.Start(instance, 5, 0));
        Assert.Equal(ScriptTimers.Capacity, rig.Timers.Count);
        rig.Timers.Clear();
        Assert.Equal(0, rig.Timers.Count);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonPositiveIntervalFiresOnTheNextAdvanceAndANegativeAdvanceMovesNothing(HostKind kind)
    {
        var rig = Setup(kind);
        var instance = Attach(rig.Host, "Timed");
        Assert.True(rig.Timers.Start(instance, -3, 4));
        Assert.True(rig.Timers.Start(instance, 1, 5));

        var due = rig.Timers.Advance(-10);

        Assert.Equal(new[] { 4 }, due.Select(entry => entry.Id).ToArray());
        Assert.Equal(1, rig.Timers.Count);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsBothNatives(HostKind kind)
    {
        var rig = Setup(kind, "UNPROVEN");

        Assert.Equal(2, rig.Report.Bound);
        Assert.All(rig.Report.Natives, outcome => Assert.Equal(AdapterState.Bound, outcome.State));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void GameTimeTimersFireOnTimerGameTimeFromTheirOwnTableAndCancel(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, Gate(),
            TestHosts.Stub("ScriptObject", null,
                "Function StartTimerGameTime(float afInterval, int aiTimerID = 0) native",
                "Function CancelTimerGameTime(int aiTimerID = 0) native"),
            TestHosts.Stub("ReferenceAlias", "ScriptObject"),
            TestHosts.Stub("Probe", null, "Function Fired(int value) global native"),
            new ScriptSpec("Hours",
                "Scriptname Hours extends ScriptObject\n" +
                "Function Run()\n  StartTimerGameTime(2.0, 3)\n  StartTimerGameTime(5.0, 4)\nEndFunction\n" +
                "Function Stop()\n  CancelTimerGameTime(4)\nEndFunction\n" +
                "Event OnTimerGameTime(int aiTimerID)\n  Probe.Fired(aiTimerID)\nEndEvent\n"),
            new ScriptSpec("AliasHours",
                "Scriptname AliasHours extends ReferenceAlias\n" +
                "Function Run()\n  StartTimerGameTime(2.0, 3)\nEndFunction\n" +
                "Event OnTimerGameTime(int aiTimerID)\n  Probe.Fired(aiTimerID)\nEndEvent\n")));
        var fired = new List<long>();
        host.RegisterNative(new NativeId("Probe", "Fired"), arguments =>
        {
            fired.Add(arguments[0].Integer);
            return PapyrusValue.None;
        });
        var timers = new ScriptTimers();
        var gameTimers = new ScriptTimers(ScriptTimers.GameTimeHandlerName);
        var report = new ReferenceTimerNativeAdapters(host, Gate(), timers, gameTimers).Bind();
        var instance = Attach(host, "Hours");

        Assert.Equal(4, report.Bound);
        Assert.True(host.RunEvent(instance, "Run").Completed);
        Assert.Equal(0, timers.Count);
        Assert.Equal(2, gameTimers.Count);
        Assert.True(host.RunEvent(instance, "Stop").Completed);
        Assert.Equal(0, gameTimers.Deliver(host, 1.5));
        Assert.Equal(1, gameTimers.Deliver(host, 0.5));
        Assert.Equal(new long[] { 3 }, fired.ToArray());
        Assert.Equal(0, gameTimers.Deliver(host, 10));

        var alias = Attach(host, "AliasHours");
        Assert.True(host.RunEvent(alias, "Run").Completed);
        Assert.Equal(1, gameTimers.Count);
        Assert.Equal(1, gameTimers.Deliver(host, 3));
        Assert.Equal(new long[] { 3, 3 }, fired.ToArray());
    }
}
