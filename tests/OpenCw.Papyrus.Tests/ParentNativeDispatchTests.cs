using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ParentNativeDispatchTests : HostFixture
{
    private static readonly NativeId Answer = new("Base", "Answer");

    private static readonly ScriptSpec BaseSpec = new("Base",
        "Scriptname Base\n" +
        "Int Function Answer(Int aiValue) native\n");

    private static readonly ScriptSpec ChildSpec = new("Child",
        "Scriptname Child extends Base\n" +
        "Int Function Run()\n" +
        "  Int got = parent.Answer(21)\n" +
        "  Observer.Record(got)\n" +
        "  Return got\n" +
        "EndFunction\n" +
        "Int Function Answer(Int aiValue)\n" +
        "  Return -1\n" +
        "EndFunction\n");

    private static readonly ScriptSpec ObserverSpec = TestHosts.Stub("Observer", null,
        "Function Record(Int aiValue) global native");

    private static EvidenceGate Gate(string? denied = null) =>
        denied is null ? TestHosts.DefaultGate() : TestHosts.GateDenying(denied);

    private IScriptHost Host(HostKind kind, string? denied = null)
    {
        var host = Track(TestHosts.Create(kind, Gate(denied), BaseSpec, ChildSpec, ObserverSpec));
        host.RegisterNative(new NativeId("Observer", "Record"), _ => PapyrusValue.None);
        return host;
    }

    private static ScriptInstance Attach(IScriptHost host)
    {
        var instance = host.Attach("Child");
        Assert.True(host.Initialize(instance).Completed);
        return instance;
    }

    public static IEnumerable<object[]> HostAndBool()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            yield return new object[] { host[0], false };
            yield return new object[] { host[0], true };
        }
    }

    public static IEnumerable<object[]> HostAndDeniedKey()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            yield return new object[] { host[0], EvidenceKeys.NativeTarget };
        }
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ParentNativeReceivesChildSelfArgumentsAndReturnsToTheCaller(HostKind kind)
    {
        var host = Host(kind);
        var instance = Attach(host);
        var calls = 0;
        host.RegisterNative(Answer, (self, arguments) =>
        {
            calls++;
            Assert.Same(instance, self.Reference);
            Assert.Equal(PapyrusValue.FromInt(21), Assert.Single(arguments));
            return PapyrusValue.FromInt(42);
        }, NativeTargetKind.ScriptInstance);

        var result = host.RunEvent(instance, "Run");

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(1, calls);
        Assert.Equal(PapyrusValue.FromInt(42), result.Value);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnboundParentNativeRefusesInsteadOfRunningAnEmptyBody(HostKind kind)
    {
        var host = Host(kind);
        var result = host.RunEvent(Attach(host), "Run");

        Assert.False(result.Completed);
    }


    [Theory]
    [MemberData(nameof(HostAndBool))]
    public void AParentNativeWaitRetainsItsAnswerAcrossDetach(HostKind kind, bool completeBeforeDetach)
    {
        var host = Host(kind);
        var calls = 0;
        var observed = new List<PapyrusValue>();
        host.RegisterNative(Answer, _ => { calls++; throw new NativeWait(1); }, NativeTargetKind.ScriptInstance);
        host.RegisterNative(new NativeId("Observer", "Record"), args =>
        {
            observed.Add(Assert.Single(args));
            return PapyrusValue.None;
        });
        var session = new CellScriptSession(host);
        var instance = session.Bind("cell", "reference", "Child");
        Assert.True(host.Initialize(instance).Completed);

        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        Assert.Equal(1, host.WaitingCount);
        Assert.Empty(observed);
        if (completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(42)));
        session.DetachCell("cell");
        if (!completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(42)));
        session.Tick(32);
        session.ReattachCell("cell");
        session.Tick(32);

        Assert.Equal(PapyrusValue.FromInt(42), Assert.Single(observed));
        Assert.Equal(1, calls);
        Assert.Equal(0, host.WaitingCount);
        Assert.False(host.CompleteLatent(1, PapyrusValue.None));
    }

    [Theory]
    [MemberData(nameof(HostAndBool))]
    public void ADirectNativeDeclarationCannotExecuteAsAnEmptyBytecodeFrame(HostKind kind, bool onInit)
    {
        var name = onInit ? "OnInit" : "Run";
        var host = Track(TestHosts.Create(kind, Gate(),
            new ScriptSpec("NativeDecl", $"Scriptname NativeDecl\nFunction {name}() native\n")));
        var instance = host.Attach("NativeDecl");
        var initialized = host.Initialize(instance);
        var result = onInit ? initialized : host.RunEvent(instance, name);

        Assert.False(result.Completed);
        Assert.Equal(onInit ? ScriptLifecycle.Failed : ScriptLifecycle.Ready, instance.Lifecycle);
    }

}
