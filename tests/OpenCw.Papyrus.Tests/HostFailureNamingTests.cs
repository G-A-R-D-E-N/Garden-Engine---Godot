using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class HostFailureNamingTests : HostFixture
{
    private static readonly ScriptSpec GoneSpec = TestHosts.Stub("Gone", null, "Function Vanish() global native");

    private static readonly ScriptSpec NeedySpec = new("Needy",
        "Scriptname Needy\n" +
        "Event OnInit()\n" +
        "  Gone.Vanish()\n" +
        "EndEvent\n");

    private static readonly ScriptSpec HolderSpec = new("HolderScript",
        "Scriptname HolderScript\n" +
        "Int Property Counter = 7 Auto\n");

    private static readonly ScriptSpec ReaderSpec = new("Reader",
        "Scriptname Reader\n" +
        "HolderScript Property Holder Auto\n" +
        "Int Property Seen Auto\n" +
        "Event OnInit()\n" +
        "  Seen = Holder.Counter\n" +
        "EndEvent\n");

    private const string HolderKey = "Q1:Test.esm";

    private RadicleScriptHost Host(params ScriptSpec[] specs) =>
        (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), specs));

    private static ScriptInstance Reader(RadicleScriptHost host) => host.Attach("Reader",
        new Dictionary<string, PapyrusValue> { ["Holder"] = PapyrusValue.FromObject(new EngineObjectReference(HolderKey)) });

    [Fact]
    public void AnUnboundNativeNamesItselfInTheInitFailure()
    {
        var host = Host(GoneSpec, NeedySpec);
        Assert.True(host.PublishUnboundRefusals() >= 1);
        var instance = host.Attach("Needy");

        var result = host.Initialize(instance);
        Assert.False(result.Completed);
        Assert.Contains("Needy", result.Message);
        Assert.Contains("Gone.Vanish", result.Message);
        Assert.Equal(ScriptLifecycle.Failed, instance.Lifecycle);
        Assert.Contains("Gone.Vanish", instance.Failure);
    }

    [Fact]
    public void WithoutTheStubRadicleDetailStillNamesTheMissingFunction()
    {
        var host = Host(GoneSpec, NeedySpec);
        var instance = host.Attach("Needy");

        Assert.False(host.Initialize(instance).Completed);
        Assert.Contains("FunctionNotFound (Gone.Vanish)", instance.Failure);
    }

    [Fact]
    public void ARealRegistrationReplacesTheStub()
    {
        var host = Host(GoneSpec, NeedySpec);
        host.PublishUnboundRefusals();
        host.RegisterNative(new NativeId("Gone", "Vanish"), _ => PapyrusValue.None);
        var instance = host.Attach("Needy");

        Assert.True(host.Initialize(instance).Completed);
    }

    [Fact]
    public void AnEngineHandlePropertyReadsTheScriptAttachedToThatForm()
    {
        var host = Host(HolderSpec, ReaderSpec);
        var holder = host.Attach("HolderScript", null, PapyrusOwnerId.ForForm(HolderKey));
        Assert.True(host.Initialize(holder).Completed);
        var reader = Reader(host);

        Assert.True(host.Initialize(reader).Completed, reader.Failure);
        Assert.True(host.TryReadScalarProperty(reader, "Seen", out var seen));
        Assert.Equal(7, seen);
    }

    [Fact]
    public void AnEngineHandlePropertyWithNoAttachedScriptNamesTheProperty()
    {
        var host = Host(HolderSpec, ReaderSpec);
        var reader = Reader(host);

        Assert.False(host.Initialize(reader).Completed);
        Assert.Contains("Counter", reader.Failure);
        Assert.Contains(HolderKey, reader.Failure);
    }
}
