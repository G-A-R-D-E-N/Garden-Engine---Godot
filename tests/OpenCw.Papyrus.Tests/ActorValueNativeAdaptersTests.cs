using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ActorValueNativeAdaptersTests : HostFixture
{
    private static readonly ScriptSpec RefSpec = TestHosts.Stub("ObjectReference", null,
        "Function SetValue(ActorValue akValue, Float afValue) native",
        "Float Function GetValue(ActorValue akValue) native",
        "Function ModValue(ActorValue akValue, Float afValue) native");

    private static readonly ScriptSpec AvSpec = TestHosts.Stub("ActorValue");

    private static readonly ScriptSpec UserSpec = new("AvUser",
        "Scriptname AvUser extends ObjectReference\n" +
        "ActorValue Property Av Auto\n" +
        "Float Property Out Auto\n" +
        "Function Put()\n" +
        "  SetValue(Av, 5.0)\n" +
        "  ModValue(Av, 2.5)\n" +
        "  Out = GetValue(Av)\n" +
        "EndFunction\n" +
        "Function ReadUnset()\n" +
        "  Out = GetValue(Av)\n" +
        "EndFunction\n");

    private (RadicleScriptHost Host, ActorValueOverlay Overlay, ScriptInstance User) Setup()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), RefSpec, AvSpec, UserSpec));
        var overlay = new ActorValueOverlay();
        Assert.Equal(3, new ActorValueNativeAdapters(host, TestHosts.DefaultGate(), overlay).Bind().Bound);
        var user = host.Attach("AvUser", new Dictionary<string, PapyrusValue> { ["Av"] = PapyrusValue.FromObject(new EngineObjectReference("0001A:Test.esm")) },
            PapyrusOwnerId.ForPlacedReference("02740E:Test.esm", "02A8A7:Test.esm"));
        return (host, overlay, user);
    }

    [Fact]
    public void ASetValueIsKeptPerReferenceAndModValueAddsToIt()
    {
        var (host, overlay, user) = Setup();

        Assert.True(host.Initialize(user).Completed, user.Failure);
        var run = host.RunEvent(user, "Put");
        Assert.True(run.Completed, run.Message + " / " + host.LastNativeFailure);
        Assert.True(host.TryReadScalarProperty(user, "Out", out var value));
        Assert.Equal(7.5f, value);
        Assert.Equal(7.5, overlay.Get("02A8A7:Test.esm", "0001A:Test.esm"));
    }

    [Fact]
    public void AValueThatWasNeverSetStopsByNameInsteadOfAnsweringZero()
    {
        var (host, _, user) = Setup();

        Assert.True(host.Initialize(user).Completed, user.Failure);
        Assert.False(host.RunEvent(user, "ReadUnset").Completed);
        Assert.Contains("0001A:Test.esm", host.LastNativeFailure);
    }

    private static double? Declared(string reference, string value, out string source)
    {
        source = "the test AVIF default";
        return value == "0001A:Test.esm" ? 45.0 : null;
    }

    [Fact]
    public void AValueThatWasNeverSetStartsFromTheHostBaseAndSaysSoOnce()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), RefSpec, AvSpec, UserSpec));
        var overlay = new ActorValueOverlay();
        var logged = new List<string>();
        new ActorValueNativeAdapters(host, TestHosts.DefaultGate(), overlay, Declared, logged.Add).Bind();
        var user = host.Attach("AvUser", new Dictionary<string, PapyrusValue> { ["Av"] = PapyrusValue.FromObject(new EngineObjectReference("0001A:Test.esm")) },
            PapyrusOwnerId.ForPlacedReference("02740E:Test.esm", "02A8A7:Test.esm"));
        Assert.True(host.Initialize(user).Completed, user.Failure);

        Assert.True(host.RunEvent(user, "ReadUnset").Completed, host.LastNativeFailure);
        Assert.True(host.RunEvent(user, "ReadUnset").Completed, host.LastNativeFailure);

        Assert.True(host.TryReadScalarProperty(user, "Out", out var value));
        Assert.Equal(45f, value);
        Assert.Single(logged);
        Assert.Contains("0001A:Test.esm", logged[0]);
        Assert.Contains("the test AVIF default", logged[0]);
        Assert.Null(overlay.Get("02A8A7:Test.esm", "0001A:Test.esm"));
    }
}
