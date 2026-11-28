using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class EventRegistrationNativeAdaptersTests : HostFixture
{
    private static readonly ScriptSpec ObjectSpec = TestHosts.Stub("ScriptObject", null,
        "Bool Function RegisterForRemoteEvent(ScriptObject akSender, String asEventName) native",
        "Function UnregisterForRemoteEvent(ScriptObject akSender, String asEventName) native",
        "Function RegisterForPlayerSleep() native",
        "Function UnregisterForPlayerSleep() native");

    private static readonly ScriptSpec ListenerSpec = new("Listener",
        "Scriptname Listener extends ScriptObject\n" +
        "ScriptObject Property Sender Auto\n" +
        "Bool Property Registered Auto\n" +
        "Event OnInit()\n" +
        "  Registered = RegisterForRemoteEvent(Sender, \"OnCripple\")\n" +
        "  RegisterForPlayerSleep()\n" +
        "EndEvent\n" +
        "Function Quit()\n" +
        "  UnregisterForRemoteEvent(Sender, \"OnCripple\")\n" +
        "EndFunction\n");

    [Fact]
    public void RegisteringIsRecordedAndAnsweredAndUnregisteringRemovesIt()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), ObjectSpec, ListenerSpec));
        var registrations = new EventRegistrations();
        Assert.Equal(12, new EventRegistrationNativeAdapters(host, TestHosts.DefaultGate(), registrations).Bind().Bound);
        var listener = host.Attach("Listener", new Dictionary<string, PapyrusValue> { ["Sender"] = PapyrusValue.FromObject(new EngineObjectReference("000014:Fallout4.esm")) });

        Assert.True(host.Initialize(listener).Completed, listener.Failure);
        Assert.True(host.TryReadScalarProperty(listener, "Registered", out var registered));
        Assert.Equal(true, registered);
        Assert.Equal(2, registrations.Count);
        Assert.Equal("000014:Fallout4.esm|OnCripple", Assert.Single(registrations.Of("RemoteEvent")).Filter);

        Assert.True(host.RunEvent(listener, "Quit").Completed);
        Assert.Empty(registrations.Of("RemoteEvent"));
        Assert.Single(registrations.Of("PlayerSleep"));
    }
}
