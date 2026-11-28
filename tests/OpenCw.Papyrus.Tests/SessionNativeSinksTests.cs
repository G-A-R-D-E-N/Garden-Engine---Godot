using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SessionNativeSinksTests
{
    private static readonly ScriptSpec DebugStub = TestHosts.Stub("Debug", null,
        "Function Trace(String text) global native",
        "Function Notification(String text) global native",
        "Function MessageBox(String text) global native");

    private static readonly ScriptSpec GameStub = TestHosts.Stub("Game", null, "Function GetPlayer() global native");

    private static GameSession Load(TempCache cache, bool traceOnInit = false)
    {
        CacheFixture.Build(cache.Root, DebugStub, GameStub, TestHosts.Stub("Probe", null,
            traceOnInit ? "Function OnInit()\n  Debug.Trace(\"initialized\")\nEndFunction" : "",
            "Function RunTrace()\n  Debug.Trace(\"probe\")\nEndFunction",
            "Function RunNotification()\n  Debug.Notification(\"probe\")\nEndFunction",
            "Function RunMessageBox()\n  Debug.MessageBox(\"probe\")\nEndFunction",
            "Function RunGetPlayer()\n  Game.GetPlayer()\nEndFunction"));
        return GameSession.Load(cache.Root);
    }

    private static ScriptInstance Ready(GameSession session)
    {
        var instance = session.Host.Attach("Probe");
        Assert.True(session.Host.Initialize(instance).Completed);
        return instance;
    }

    [Fact]
    public void CacheLoadedOnInitReachesTheTraceSinkBecauseRadicleDoesNotGateParameters()
    {
        using var cache = new TempCache();
        using var session = Load(cache, traceOnInit: true);
        var instance = session.Host.Attach("Probe");
        var result = session.Host.Initialize(instance);

        Assert.True(result.Completed, TestHosts.Describe(session.Host, result));
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.Equal("initialized", Assert.Single(session.NativeSinks.Traces));
        Assert.True(session.Host.Initialize(instance).Completed);
        Assert.Single(session.NativeSinks.Traces);
        Assert.Equal(0, TestHosts.NativeTargetRefusals(session.Host));
    }


    [Fact]
    public void LoadingBaseSinksDoesNotFabricateOtherNativeBindings()
    {
        using var cache = new TempCache();
        using var session = Load(cache);
        var result = session.Host.RunEvent(Ready(session), "RunGetPlayer");

        Assert.False(result.Completed, TestHosts.Describe(session.Host, result));
        Assert.Empty(session.NativeSinks.Traces);
        Assert.Equal(3, session.NativeSinks.Count);
    }

    [Theory]
    [InlineData("Trace", "probe")]
    [InlineData("Notification", "notification: probe")]
    [InlineData("MessageBox", "messagebox: probe")]
    public void CacheLoadedSessionReportsItsAllowedDebugCalls(string function, string expected)
    {
        using var cache = new TempCache();
        using var session = Load(cache);
        var result = session.Host.RunEvent(Ready(session), "Run" + function);

        Assert.True(result.Completed, TestHosts.Describe(session.Host, result));
        var sinks = session.NativeSinks;
        Assert.Equal(expected, Assert.Single(sinks.Traces), ignoreCase: true);
        Assert.Equal(3, sinks.Count);
        Assert.Equal(0, TestHosts.NativeTargetRefusals(session.Host));
        Assert.Single(session.Host.Instances);
    }
}
