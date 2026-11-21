using System.Text;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class RuntimeTablesSaveTests : HostFixture
{
    private static readonly PapyrusOwnerId Lamp = PapyrusOwnerId.ForPlacedReference("000800:Base.esm", "000A00");

    private sealed class NoClips : IAnimationClips
    {
        public AnimationClip? Find(string refKey, string animation) => null;
    }

    private sealed record Rig(IScriptHost Host, ScriptInstance Instance, List<long> Fired);

    private Rig Setup(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(),
            TestHosts.Stub("ScriptObject", null),
            TestHosts.Stub("ObjectReference", "ScriptObject"),
            TestHosts.Stub("Probe", null, "Function Fired(int value) global native"),
            new ScriptSpec("Timed",
                "Scriptname Timed extends ObjectReference\n" +
                "Event OnTimer(int aiTimerID)\n  Probe.Fired(aiTimerID)\nEndEvent\n" +
                "Event OnAnimationEvent(ObjectReference akSource, string asEventName)\n  Probe.Fired(99)\nEndEvent\n")));
        var fired = new List<long>();
        host.RegisterNative(new NativeId("Probe", "Fired"), arguments =>
        {
            fired.Add(arguments[0].Integer);
            return PapyrusValue.None;
        });
        var instance = host.Attach("Timed", null, Lamp);
        Assert.True(host.Initialize(instance).Completed);
        return new Rig(host, instance, fired);
    }

    private static RuntimeTables RoundTrip(ScriptTimers timers, ReferenceAnimations animations) =>
        RuntimeTablesSave.Read(RuntimeTablesSave.Sections(timers, animations))!;

    private static ReferenceAnimations Animations() => new(new NoClips());

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ATimerKeepsItsRemainingTimeAcrossASave(HostKind kind)
    {
        var rig = Setup(kind);
        var timers = new ScriptTimers();
        Assert.True(timers.Start(rig.Instance, 1.5, 7));
        timers.Deliver(rig.Host, 0.5);

        var tables = RoundTrip(timers, Animations());
        var loaded = new ScriptTimers();
        var report = RuntimeTablesSave.Apply(tables, loaded, Animations(), rig.Host.Instances);

        Assert.Equal(1, report.Timers);
        Assert.Equal(1, loaded.Count);
        Assert.Equal(0, loaded.Deliver(rig.Host, 0.9));
        Assert.Equal(1, loaded.Deliver(rig.Host, 0.2));
        Assert.Equal(new long[] { 7 }, rig.Fired.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TimersFireInTheOrderTheyWereDue(HostKind kind)
    {
        var rig = Setup(kind);
        var timers = new ScriptTimers();
        timers.Start(rig.Instance, 2.0, 1);
        timers.Start(rig.Instance, 1.0, 2);

        var tables = RoundTrip(timers, Animations());
        var loaded = new ScriptTimers();
        RuntimeTablesSave.Apply(tables, loaded, Animations(), rig.Host.Instances);
        loaded.Deliver(rig.Host, 5);

        Assert.Equal(new long[] { 2, 1 }, rig.Fired.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ATimerForAScriptThatAttachesLaterWaitsAndThenFires(HostKind kind)
    {
        var rig = Setup(kind);
        var timers = new ScriptTimers();
        timers.Start(rig.Instance, 1.0, 4);
        var tables = RoundTrip(timers, Animations());

        var loaded = new ScriptTimers();
        Assert.Equal(1, RuntimeTablesSave.Apply(tables, loaded, Animations(), new List<ScriptInstance>()).Timers);

        Assert.Equal(0, loaded.Count);
        Assert.Equal(1, loaded.Waiting);
        Assert.Equal(1, loaded.Deliver(rig.Host, 1.0));
        Assert.Equal(0, loaded.Waiting);
        Assert.Equal(new long[] { 4 }, rig.Fired.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ATimerOnAParkedInstanceIsKeptUntilTheCellReturns(HostKind kind)
    {
        var rig = Setup(kind);
        var session = new CellScriptSession(rig.Host);
        var owner = PapyrusOwnerId.ForPlacedReference("000800:Base.esm", "000C00");
        var bound = session.Bind("Cell", owner.Key, "Timed", null, owner);
        Assert.True(rig.Host.Initialize(bound).Completed);
        var timers = new ScriptTimers();
        timers.Start(bound, 1.0, 5);
        session.DetachCell("Cell");

        Assert.Equal(0, timers.Deliver(rig.Host, 2.0));
        Assert.Equal(0, timers.Skipped);
        Assert.Equal(1, timers.Count);

        session.ReattachCell("Cell");
        Assert.Equal(1, timers.Deliver(rig.Host, 0));
        Assert.Equal(new long[] { 5 }, rig.Fired.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnAnimationRegistrationSurvivesASave(HostKind kind)
    {
        var rig = Setup(kind);
        var animations = Animations();
        Assert.True(animations.Register(rig.Instance, PapyrusValue.FromObject(new EngineObjectReference("lever")), "lever", "done"));

        var tables = RoundTrip(new ScriptTimers(), animations);
        var loaded = Animations();
        var report = RuntimeTablesSave.Apply(tables, new ScriptTimers(), loaded, rig.Host.Instances);

        Assert.Equal(1, report.Registrations);
        Assert.Equal(1, loaded.Registered);
        Assert.Equal(Lamp.Key, tables.Registrations[0].Owner);
        Assert.Equal("lever", tables.Registrations[0].SourceKey);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ARegistrationForAScriptThatAttachesLaterWaits(HostKind kind)
    {
        var rig = Setup(kind);
        var animations = Animations();
        animations.Register(rig.Instance, PapyrusValue.FromObject(new EngineObjectReference("lever")), "lever", "done");
        var tables = RoundTrip(new ScriptTimers(), animations);

        var loaded = Animations();
        RuntimeTablesSave.Apply(tables, new ScriptTimers(), loaded, new List<ScriptInstance>());

        Assert.Equal(0, loaded.Registered);
        Assert.Equal(1, loaded.Waiting);
        loaded.Deliver(rig.Host, 0);
        Assert.Equal(1, loaded.Registered);
        Assert.Equal(0, loaded.Waiting);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ATimerOnAScriptWithNoOwnerIsCountedNotSaved(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(),
            TestHosts.Stub("ScriptObject", null),
            TestHosts.Stub("Plain", "ScriptObject")));
        var instance = host.Attach("Plain");
        var timers = new ScriptTimers();
        timers.Start(instance, 1.0, 1);

        var tables = RoundTrip(timers, Animations());

        Assert.Empty(tables.Timers);
        Assert.Equal(1, timers.Unnamed);
    }

    [Fact]
    public void ASaveWithoutTheSectionReadsAsNull()
    {
        Assert.Null(RuntimeTablesSave.Read(new Dictionary<string, byte[]>()));
    }

    [Theory]
    [InlineData("{\"version\":2,\"timers\":[],\"animationRegistrations\":[]}")]
    [InlineData("{\"version\":1,\"timers\":[{\"owner\":\"a\",\"script\":\"b\",\"id\":1,\"remaining\":-1}],\"animationRegistrations\":[]}")]
    [InlineData("{\"version\":1,\"timers\":[{\"owner\":\"\",\"script\":\"b\",\"id\":1,\"remaining\":1}],\"animationRegistrations\":[]}")]
    [InlineData("{\"version\":1,\"timers\":[{\"owner\":\"a\",\"script\":\"b\",\"id\":\"x\",\"remaining\":1}],\"animationRegistrations\":[]}")]
    [InlineData("{\"version\":1,\"timers\":[]}")]
    [InlineData("not json")]
    public void AMalformedSectionIsRefused(string json)
    {
        var sections = new Dictionary<string, byte[]> { [RuntimeTablesSave.SectionName] = Encoding.UTF8.GetBytes(json) };

        Assert.Throws<FormatException>(() => RuntimeTablesSave.Read(sections));
    }

    [Fact]
    public void TooManyTimersAreRefused()
    {
        var rows = string.Join(",", Enumerable.Range(0, ScriptTimers.Capacity + 1).Select(id => $"{{\"owner\":\"a\",\"script\":\"b\",\"id\":{id},\"remaining\":1}}"));
        var sections = new Dictionary<string, byte[]> { [RuntimeTablesSave.SectionName] = Encoding.UTF8.GetBytes($"{{\"version\":1,\"timers\":[{rows}],\"animationRegistrations\":[]}}") };

        Assert.Throws<FormatException>(() => RuntimeTablesSave.Read(sections));
    }
}
