using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class UnsupportedNativeAdaptersTests : HostFixture
{
    private static readonly NativeId SceneStart = new("Scene", "Start");

    private IScriptHost Host(HostKind kind, EvidenceGate gate) => Track(TestHosts.Create(kind, gate,
        TestHosts.Stub("Scene", null, "Function Start() native"),
        TestHosts.Stub("ScriptObject", null, "Function RegisterForCustomEvent(string asEvent) native"),
        TestHosts.Stub("Probe", null, "Scene Function Make() global native"),
        new ScriptSpec("Caller", "Scriptname Caller extends ScriptObject\nFunction Run()\n  Scene target = Probe.Make()\n  target.Start()\nEndFunction\nFunction Register()\n  RegisterForCustomEvent(\"Done\")\nEndFunction\n")));

    private static (IScriptHost Host, AdapterReport Report, List<string> Logged) Setup(UnsupportedNativeAdaptersTests self, HostKind kind)
    {
        var gate = TestHosts.Gate((UnsupportedNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = self.Host(kind, gate);
        host.RegisterNative(new NativeId("Probe", "Make"), _ => PapyrusValue.FromObject(new EngineObjectReference("000A00:Base.esm")));
        var logged = new List<string>();
        var report = new UnsupportedNativeAdapters(host, gate, logged.Add).Bind(UnsupportedNativeAdapters.Table.Where(entry =>
            entry.Function is "Start" or "RegisterForCustomEvent").ToList());
        return (host, report, logged);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AMemberNativeOnAnEngineObjectStopsAndNamesTheMissingSystem(HostKind kind)
    {
        var (host, report, logged) = Setup(this, kind);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Run");

        Assert.False(result.Completed);
        Assert.All(report.Natives, outcome => Assert.Equal(AdapterState.Unsupported, outcome.State));
        Assert.Equal(new[] { "Scene.Start is not supported: Garden has no scene system" }, logged);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AMemberNativeOnAScriptInstanceStopsAndNamesTheMissingSystem(HostKind kind)
    {
        var (host, _, logged) = Setup(this, kind);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Register");

        Assert.False(result.Completed);
        Assert.Equal(new[] { "ScriptObject.RegisterForCustomEvent is not supported: Garden has no custom event sender" }, logged);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANativeARealAdapterAlreadyBoundIsNotShadowed(HostKind kind)
    {
        var gate = TestHosts.Gate((UnsupportedNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = Host(kind, gate);
        host.TryRegisterNative(SceneStart, (receiver, arguments) => PapyrusValue.None, NativeTargetKind.EngineObject);

        var report = new UnsupportedNativeAdapters(host, gate, _ => { }).Bind(UnsupportedNativeAdapters.Table.Where(entry => entry.Function == "Start").ToList());

        Assert.Equal(AdapterState.Conflict, Assert.Single(report.Natives).State);
    }
}
