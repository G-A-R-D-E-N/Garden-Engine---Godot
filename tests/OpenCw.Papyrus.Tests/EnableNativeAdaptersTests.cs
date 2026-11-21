using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class EnableNativeAdaptersTests : HostFixture
{
    private static readonly PapyrusOwnerId Volume = PapyrusOwnerId.ForPlacedReference("000800:Base.esp", "000A00");

    private static PapyrusValue Ref(PapyrusOwnerId owner) => PapyrusValue.FromObject(new EngineObjectReference(owner));

    private IScriptHost Host(HostKind kind, EvidenceGate gate, PapyrusValue receiver, string body)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("ObjectReference", null,
                "Function Enable(bool abFadeIn = false) native\n" +
                "Function Disable(bool abFadeOut = false) native\n" +
                "Function EnableNoWait(bool abFadeIn = false) native\n" +
                "Function DisableNoWait(bool abFadeOut = false) native"),
            TestHosts.Stub("ReceiverSource", null, "ObjectReference Function Target() global native"),
            new ScriptSpec("Caller",
                $"Scriptname Caller\nFunction Run()\n  ObjectReference target = ReceiverSource.Target()\n  {body}\nEndFunction\n")));
        host.RegisterNative(new NativeId("ReceiverSource", "Target"), _ => receiver);
        return host;
    }

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void EnableAndDisableWriteTheStateKeyedByTheReference(HostKind kind)
    {
        var calls = new List<(string Key, bool Enabled)>();
        var gate = TestHosts.Gate((EnableNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Ref(Volume), "target.Disable()\n  target.EnableNoWait(True)\n  target.Disable(True)\n  target.Enable()");
        var report = new EnableNativeAdapters(host, gate, (key, enabled) => calls.Add((key, enabled))).Bind();

        var result = Run(host);

        Assert.Equal(4, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { ("000A00", false), ("000A00", true), ("000A00", false), ("000A00", true) }, calls.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void DisableLeavesTheReferenceDisabled(HostKind kind)
    {
        var states = new Dictionary<string, bool>();
        var gate = TestHosts.Gate((EnableNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Ref(Volume), "target.Enable()\n  target.DisableNoWait()");
        new EnableNativeAdapters(host, gate, (key, enabled) => states[key] = enabled).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.False(states["000A00"]);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AScriptPropertyHoldingTheBareReferenceKeyIsTheSameTarget(HostKind kind)
    {
        var calls = new List<(string Key, bool Enabled)>();
        var gate = TestHosts.Gate((EnableNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, PapyrusValue.FromObject(new EngineObjectReference("000A00:Base.esp")), "target.Enable()");
        new EnableNativeAdapters(host, gate, (key, enabled) => calls.Add((key, enabled))).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { ("000A00:Base.esp", true) }, calls.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnIdentityThatIsNotAReferenceKeyIsRefusedAndWritesNothing(HostKind kind)
    {
        var calls = new List<(string Key, bool Enabled)>();
        var gate = TestHosts.Gate((EnableNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, PapyrusValue.FromObject(new EngineObjectReference("no-colon")), "target.Enable()");
        new EnableNativeAdapters(host, gate, (key, enabled) => calls.Add((key, enabled))).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(calls);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsTheNatives(HostKind kind)
    {
        var gate = TestHosts.Gate((EnableNativeAdapters.SystemKey, "UNPROVEN"));
        var host = Host(kind, gate, Ref(Volume), "target.Enable()");

        var report = new EnableNativeAdapters(host, gate, (_, _) => { }).Bind();

        Assert.Equal(4, report.Bound);
        Assert.All(report.Natives, outcome => Assert.Equal(AdapterState.Bound, outcome.State));
    }
}
