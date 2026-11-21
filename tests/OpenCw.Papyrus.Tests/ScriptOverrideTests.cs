using System;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptOverrideTests : HostFixture
{
    private const string Handle = "Handle";
    private const string OnInitEmpty = "Function OnInit()\nEndFunction\n";
    private const string HandleEmpty = "Function Handle()\nEndFunction\n";

    private static readonly ScriptSpec DebugStub = TestHosts.Stub("Debug", null,
        "Function Trace(String asText) global native",
        "Function Notification(String asText) global native",
        "Function MessageBox(String asText) global native");

    private static ScriptOverrideRegistry Overrides(IScriptHost host)
    {
        var registry = new ScriptOverrideRegistry(host.Gate);
        host.Overrides = registry;
        return registry;
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void HandledOverrideReplacesBytecode(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate((EvidenceKeys.Assignment, "UNPROVEN")),
            TestHosts.Stub("QuestScript", null, OnInitEmpty, HandleEmpty)));
        var overrides = Overrides(host);
        var instance = host.Attach("QuestScript");
        Assert.True(host.Initialize(instance).Completed);

        overrides.Register("QuestScript", Handle, 0, _ => OverrideResult.Handled(PapyrusValue.FromInt(7)));
        var result = host.RunEvent(instance, Handle);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(7, result.Value.Integer);
        Assert.Equal(1, overrides.HandledCount);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void DeclinedOverrideFallsThroughToBytecode(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(),
            TestHosts.Stub("Marker", null, "Function Mark() global native"),
            TestHosts.Stub("QuestScript", null, OnInitEmpty, "Function Handle()\n  Marker.Mark()\nEndFunction\n")));
        var marks = 0;
        host.RegisterNative(new NativeId("Marker", "Mark"), _ =>
        {
            marks++;
            return PapyrusValue.None;
        });
        var overrides = Overrides(host);
        var instance = host.Attach("QuestScript");
        Assert.True(host.Initialize(instance).Completed);

        overrides.Register("QuestScript", Handle, 0, _ => OverrideResult.Declined("not mine"));
        var result = host.RunEvent(instance, Handle);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(1, overrides.DeclinedCount);
        Assert.Equal(1, marks);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonexistentEventIsRefused(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(),
            TestHosts.Stub("QuestScript", null, OnInitEmpty)));
        var overrides = Overrides(host);
        var instance = host.Attach("QuestScript");
        Assert.True(host.Initialize(instance).Completed);

        var missing = host.RunEvent(instance, "NotAnEvent");
        Assert.False(missing.Completed);
        Assert.Contains("no event named", missing.Message!, StringComparison.Ordinal);
        Assert.Equal(1, TestHosts.RefusedEvents(host));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void WrongArgumentCountFailsBeforeTheOverrideRuns(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(),
            TestHosts.Stub("QuestScript", null, OnInitEmpty, "Function Handle(Int first, Int second)\nEndFunction\n")));
        var overrides = Overrides(host);
        var instance = host.Attach("QuestScript");
        Assert.True(host.Initialize(instance).Completed);

        var ran = false;
        overrides.Register("QuestScript", Handle, 2, _ =>
        {
            ran = true;
            return OverrideResult.Handled(PapyrusValue.None);
        });

        var result = host.RunEvent(instance, Handle, PapyrusValue.FromInt(1));

        Assert.False(result.Completed);
        Assert.False(ran);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AMethodReturnBecomesTheCallResult(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(),
            TestHosts.Stub("QuestScript", null, OnInitEmpty, HandleEmpty)));
        var overrides = Overrides(host);
        var instance = host.Attach("QuestScript");
        Assert.True(host.Initialize(instance).Completed);

        overrides.Register("QuestScript", Handle, 0, _ => OverrideResult.Handled(PapyrusValue.FromString("handled")));
        var result = host.RunEvent(instance, Handle);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal("handled", result.Value.Text);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ASynchronousOnInitOverrideReleasesTheBarrier(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(),
            TestHosts.Stub("Utility", null, "Function Wait(Float afSeconds) global native"),
            TestHosts.Stub("QuestScript", null, "Function OnInit()\n  Utility.Wait(1.0)\nEndFunction\n", HandleEmpty)));
        host.RegisterNative(new NativeId("Utility", "Wait"), _ => throw new NativeWait(7));
        var overrides = Overrides(host);
        var instance = host.Attach("QuestScript");

        overrides.Register("QuestScript", "OnInit", 0, _ => OverrideResult.Handled(PapyrusValue.None));
        var initialized = host.Initialize(instance);

        Assert.True(initialized.Completed, TestHosts.Describe(host, initialized));
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.Equal(1, overrides.HandledCount);
        Assert.True(host.RunEvent(instance, Handle).Completed);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void OverridesNeverInterceptNatives(HostKind kind)
    {
        var gate = TestHosts.Gate((NativeSet.TraceKey, "ENGINE_POLICY"), (NativeSet.NotificationKey, "ENGINE_POLICY"),
            (NativeSet.MessageBoxKey, "ENGINE_POLICY"));
        var host = Track(TestHosts.Create(kind, gate, DebugStub,
            TestHosts.Stub("Caller", null, OnInitEmpty, "Function Handle(String text)\n  Debug.Trace(text)\nEndFunction\n")));
        var natives = new NativeSet(host.Gate);
        natives.BindBaseSet();
        natives.Install(host);
        var overrides = Overrides(host);
        overrides.Register("Debug", "Trace", ScriptOverrideRegistry.AnyParameterCount,
            _ => OverrideResult.Handled(PapyrusValue.None));

        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        var result = host.RunEvent(instance, Handle, PapyrusValue.FromString("native"));

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { "native" }, natives.Traces);
        Assert.Equal(0, overrides.HandledCount);
    }
}
