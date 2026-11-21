using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReferenceOpenNativeAdaptersTests : HostFixture
{
    private static readonly PapyrusOwnerId Door = PapyrusOwnerId.ForPlacedReference("000800:Base.esm", "000A00");

    private sealed class Doors : IReferenceOpenAccess
    {
        public Dictionary<string, bool> State { get; } = new();

        public bool TrySetOpen(string refKey, bool open)
        {
            if (!State.ContainsKey(refKey)) return false;
            State[refKey] = open;
            return true;
        }

        public bool TryGetOpen(string refKey, out bool open) => State.TryGetValue(refKey, out open);
    }

    private IScriptHost Host(HostKind kind, EvidenceGate gate, PapyrusValue receiver, string body, List<long> states)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("ObjectReference", null,
                "Function SetOpen(bool abOpen = true) native",
                "int Function GetOpenState() native"),
            TestHosts.Stub("Probe", null,
                "ObjectReference Function Self() global native",
                "Function ReportState(int value) global native"),
            new ScriptSpec("Caller", $"Scriptname Caller\nFunction Run()\n  ObjectReference target = Probe.Self()\n  {body}\nEndFunction\n")));
        host.RegisterNative(new NativeId("Probe", "Self"), _ => receiver);
        host.RegisterNative(new NativeId("Probe", "ReportState"), arguments =>
        {
            states.Add(arguments[0].Integer);
            return PapyrusValue.None;
        });
        return host;
    }

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    private static EvidenceGate Gate(string status = "APPROXIMATED") => TestHosts.Gate((ReferenceOpenNativeAdapters.SystemKey, status));

    private static PapyrusValue Ref(PapyrusOwnerId owner) => PapyrusValue.FromObject(new EngineObjectReference(owner));

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void SetOpenWritesTheOpenFlagOfTheReferenceAndDefaultsToOpen(HostKind kind)
    {
        var doors = new Doors { State = { ["000A00"] = false } };
        var states = new List<long>();
        var gate = Gate();
        var host = Host(kind, gate, Ref(Door), "target.SetOpen()\n  Probe.ReportState(target.GetOpenState())\n  target.SetOpen(False)\n  Probe.ReportState(target.GetOpenState())", states);
        var report = new ReferenceOpenNativeAdapters(host, gate, doors).Bind();

        var result = Run(host);

        Assert.Equal(2, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new long[] { ReferenceOpenNativeAdapters.StateOpen, ReferenceOpenNativeAdapters.StateClosed }, states.ToArray());
        Assert.False(doors.State["000A00"]);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AReferenceWithNoOpenStateAnswersNoneAndSetOpenIsCountedNotApplied(HostKind kind)
    {
        var doors = new Doors();
        var states = new List<long>();
        var gate = Gate();
        var host = Host(kind, gate, Ref(Door), "target.SetOpen(True)\n  Probe.ReportState(target.GetOpenState())", states);
        var adapters = new ReferenceOpenNativeAdapters(host, gate, doors);
        adapters.Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new long[] { ReferenceOpenNativeAdapters.StateNone }, states.ToArray());
        Assert.Equal(1, adapters.Unapplied);
        Assert.Empty(doors.State);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void WithNoDoorAnswerAnAnimatedActivatorReadsOpeningThenOpenFromTheClipTable(HostKind kind)
    {
        var clips = new Clips { Known = { [("000A00", "Open")] = new AnimationClip("Open", 1f, []) } };
        var table = new ReferenceAnimations(clips);
        var states = new List<long>();
        var gate = Gate();
        var host = Host(kind, gate, Ref(Door), "Probe.ReportState(target.GetOpenState())", states);
        new ReferenceOpenNativeAdapters(host, gate, new Doors(), table).Bind();
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        Assert.True(host.RunEvent(instance, "Run").Completed);
        table.Play("000A00", "Open");
        Assert.True(host.RunEvent(instance, "Run").Completed);
        table.Advance(2);
        Assert.True(host.RunEvent(instance, "Run").Completed);

        Assert.Equal(new long[]
        {
            ReferenceOpenNativeAdapters.StateNone, ReferenceOpenNativeAdapters.StateOpening, ReferenceOpenNativeAdapters.StateOpen,
        }, states.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ALoadedDoorReadsOpeningWhileItsClipPlaysThenTheRecordedState(HostKind kind)
    {
        var clips = new Clips { Known = { [("000A00", "Open")] = new AnimationClip("Open", 1f, []) } };
        var table = new ReferenceAnimations(clips);
        var doors = new Doors { State = { ["000A00"] = true } };
        var states = new List<long>();
        var gate = Gate();
        var host = Host(kind, gate, Ref(Door), "Probe.ReportState(target.GetOpenState())", states);
        new ReferenceOpenNativeAdapters(host, gate, doors, table).Bind();
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        table.Play("000A00", "Open");
        Assert.True(host.RunEvent(instance, "Run").Completed);
        table.Advance(2);
        Assert.True(host.RunEvent(instance, "Run").Completed);

        Assert.Equal(new long[] { ReferenceOpenNativeAdapters.StateOpening, ReferenceOpenNativeAdapters.StateOpen }, states.ToArray());
    }

    private sealed class Clips : IAnimationClips
    {
        public Dictionary<(string, string), AnimationClip> Known { get; } = new();

        public AnimationClip? Find(string refKey, string animation) => Known.GetValueOrDefault((refKey, animation));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AScriptPropertyReferenceIsKeyedByItsBareFormKey(HostKind kind)
    {
        var doors = new Doors { State = { ["000A00:Base.esm"] = true } };
        var states = new List<long>();
        var gate = Gate();
        var host = Host(kind, gate, PapyrusValue.FromObject(new EngineObjectReference("000A00:Base.esm")), "Probe.ReportState(target.GetOpenState())", states);
        new ReferenceOpenNativeAdapters(host, gate, doors).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new long[] { ReferenceOpenNativeAdapters.StateOpen }, states.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnIdentityThatIsNotAReferenceKeyIsRefusedAndWritesNothing(HostKind kind)
    {
        var doors = new Doors { State = { ["no-colon"] = false } };
        var states = new List<long>();
        var gate = Gate();
        var host = Host(kind, gate, PapyrusValue.FromObject(new EngineObjectReference("no-colon")), "target.SetOpen(True)", states);
        new ReferenceOpenNativeAdapters(host, gate, doors).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.False(doors.State["no-colon"]);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsBothNatives(HostKind kind)
    {
        var gate = Gate("UNPROVEN");
        var host = Host(kind, gate, Ref(Door), "target.SetOpen()", new List<long>());

        var report = new ReferenceOpenNativeAdapters(host, gate, new Doors()).Bind();

        Assert.Equal(2, report.Bound);
        Assert.All(report.Natives, outcome => Assert.Equal(AdapterState.Bound, outcome.State));
    }
}
