using System;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class CellScriptSessionTests : HostFixture
{
    private const string RefScriptSource =
        "Scriptname RefScript\n" +
        "Function OnInit()\n" +
        "EndFunction\n" +
        "Function OnActivate()\n" +
        "  Int n = 0\n" +
        "EndFunction\n";

    private static readonly ScriptSpec RefScript = new("RefScript", RefScriptSource);

    private IScriptHost Host(HostKind kind) => Track(TestHosts.Create(kind, TestHosts.Gate(), RefScript));

    private (CellScriptSession Session, IScriptHost Host) Session(HostKind kind)
    {
        var host = Host(kind);
        return (new CellScriptSession(host), host);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void PlayedAndStreamedCellsShareOneHost(HostKind kind)
    {
        var (session, host) = Session(kind);
        var played = session.Bind("PlayedCell", "000800:Played.esp", "RefScript");
        var streamed = session.Bind("StreamedCell", "000900:Streamed.esp", "RefScript");

        Assert.Same(host, session.Host);
        Assert.Same(played, session.InstanceFor("000800:Played.esp"));
        Assert.Same(streamed, session.InstanceFor("000900:Streamed.esp"));
        Assert.Equal(2, session.BoundInstances);
        Assert.Equal(2, host.Instances.Count);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void DetachParksAndReattachReusesTheInstance(HostKind kind)
    {
        var (session, host) = Session(kind);
        var instance = session.Bind("StreamedCell", "000900:Streamed.esp", "RefScript");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);

        var parked = session.DetachCell("StreamedCell");

        Assert.Same(instance, Assert.Single(parked));
        Assert.Null(session.InstanceFor("000900:Streamed.esp"));
        Assert.Equal(1, session.ParkedInstances);
        Assert.Equal(0, session.BoundInstances);

        var restored = session.ReattachCell("StreamedCell");

        Assert.Same(instance, Assert.Single(restored));
        Assert.Same(instance, session.InstanceFor("000900:Streamed.esp"));
        Assert.Equal(1, session.ReusedInstances);
        Assert.Equal(0, session.ParkedInstances);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ActivationReachesTheScript(HostKind kind)
    {
        var (session, host) = Session(kind);
        var instance = session.Bind("PlayedCell", "000800:Played.esp", "RefScript");
        Assert.True(host.Initialize(instance).Completed);

        var activation = session.Activate("000800:Played.esp", "OnActivate");

        Assert.True(activation.Reached);
        Assert.NotNull(activation.Result);
        Assert.True(activation.Result!.Completed, TestHosts.Describe(host, activation.Result));
        Assert.Equal(1, session.Activations);
        Assert.Equal(0, session.RefusedActivations);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ActivationForAParkedReferenceIsRefused(HostKind kind)
    {
        var (session, host) = Session(kind);
        var instance = session.Bind("StreamedCell", "000900:Streamed.esp", "RefScript");
        Assert.True(host.Initialize(instance).Completed);
        session.DetachCell("StreamedCell");

        var activation = session.Activate("000900:Streamed.esp", "OnActivate");

        Assert.False(activation.Reached);
        Assert.Contains("parked", activation.Reason!, StringComparison.Ordinal);
        Assert.Equal(1, session.RefusedActivations);
        Assert.Equal(0, session.Activations);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ActivationForAnUnknownReferenceIsRefused(HostKind kind)
    {
        var (session, _) = Session(kind);

        var activation = session.Activate("000A00:Unknown.esp", "OnActivate");

        Assert.False(activation.Reached);
        Assert.Contains("not bound", activation.Reason!, StringComparison.Ordinal);
        Assert.Equal(1, session.RefusedActivations);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TickOnlyDelegatesToTheHost(HostKind kind)
    {
        var host = Track(TestHosts.Create(kind, TestHosts.Gate(), new ScriptSpec("Waiter",
            "Scriptname Waiter\n" +
            "Int Function Park() global native\n" +
            "Function OnActivate()\n" +
            "  Int got = Park()\n" +
            "  Int n = got\n" +
            "EndFunction\n")));
        host.RegisterNative(new NativeId("Waiter", "Park"), _ => throw new NativeWait(1));
        var session = new CellScriptSession(host);
        var instance = session.Bind("PlayedCell", "000800:Played.esp", "Waiter");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.Waiting, session.Activate("000800:Played.esp", "OnActivate").Result!.Reason);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(3)));

        var spent = session.Tick(16);

        Assert.Equal(1, spent);
        Assert.Equal(0, host.WaitingCount);
        Assert.Equal(0, TestHosts.Counters(host).Ready);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void CellSessionRunsItsEventThroughTheInterfaceTypedHost(HostKind kind)
    {
        IScriptHost host = Host(kind);
        var session = new CellScriptSession(host);
        var instance = session.Bind("PlayedCell", "000800:Played.esp", "RefScript");
        Assert.True(host.Initialize(instance).Completed);

        var activation = session.Activate("000800:Played.esp", "OnActivate");

        Assert.True(activation.Reached);
        Assert.True(activation.Result!.Completed, TestHosts.Describe(host, activation.Result));
        Assert.Same(host, session.Host);
    }
}
