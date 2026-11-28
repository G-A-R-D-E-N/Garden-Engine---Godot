using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class NativeFailureNamingTests : HostFixture
{
    private static readonly ScriptSpec Boom = TestHosts.Stub("Boom", null, "Function Explode() global native");

    private static readonly ScriptSpec Caller = new("Caller",
        "Scriptname Caller\n" +
        "Function Go()\n" +
        "  Boom.Explode()\n" +
        "EndFunction\n");

    private static readonly ScriptSpec InitCaller = new("InitCaller",
        "Scriptname InitCaller\n" +
        "Event OnInit()\n" +
        "  Boom.Explode()\n" +
        "EndEvent\n");

    private RadicleScriptHost Create()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), Boom, Caller, InitCaller));
        host.RegisterNative(new NativeId("Boom", "Explode"), _ => throw new InvalidOperationException("kaboom"));
        return host;
    }

    private static int Count(string? text, string part) => text is null ? 0 : text.Split(part).Length - 1;

    [Fact]
    public void AFailedCallNamesTheNativeFailureOnce()
    {
        var host = Create();
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Go");

        Assert.False(result.Completed);
        Assert.Equal(1, Count(result.Message, "kaboom"));
    }

    [Fact]
    public void ALaterFailureOfAnotherCallDoesNotInheritAnEarlierNativeFailure()
    {
        var host = Create();
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.False(host.RunEvent(instance, "Go").Completed);

        var missing = host.RunEvent(instance, "NotDeclared");

        Assert.False(missing.Completed);
        Assert.Equal(0, Count(missing.Message, "kaboom"));
    }

    [Fact]
    public void ADeferredOnInitFailureNamesTheNativeFailureOnce()
    {
        var host = Create();
        var instance = host.Attach("InitCaller");

        var result = host.Initialize(instance);

        Assert.False(result.Completed);
        Assert.Equal(1, Count(instance.Failure, "kaboom"));
        Assert.Equal(1, Count(result.Message, "kaboom"));
    }
}
