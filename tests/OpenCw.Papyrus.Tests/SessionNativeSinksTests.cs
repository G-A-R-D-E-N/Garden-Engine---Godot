using System.Text;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SessionNativeSinksTests
{
    private static GameSession Load(TempCache cache, bool traceOnInit = false)
    {
        var bytes = Encoding.UTF8.GetBytes(PapyrusIr.ToJson(new[]
        {
            new PapyrusScript("Probe", traceOnInit ? new[] { new PapyrusFunction("OnInit", new[]
            {
                PapyrusInstruction.CallStatic("Debug", "Trace", PapyrusOperand.Discard,
                    new[] { PapyrusOperand.Constant(PapyrusValue.FromString("initialized")) }),
                PapyrusInstruction.Return(),
            }) } : Array.Empty<PapyrusFunction>(), Array.Empty<string>()),
        }));
        ScriptCacheBuilder.Build(new[]
        {
            new ScriptSourceCandidate("Probe", ScriptSourceKind.Loose, "project-authored", 0,
                bytes.Length, () => bytes),
        }, "project-authored", new ScriptCacheConverter(1, 1, payload => Encoding.UTF8.GetString(payload)), cache.Root);
        return GameSession.Load(cache.Root);
    }

    [Fact]
    public void CacheLoadedSinksDoNotBypassUnprovenParameters()
    {
        using var cache = new TempCache();
        var session = Load(cache, traceOnInit: true);
        var instance = session.Host.Attach("Probe");
        var result = session.Host.Initialize(instance);

        Assert.Equal(VmStopReason.GateStopped, result.Reason);
        Assert.Equal(EvidenceKeys.Parameters, result.Key);
        Assert.Equal(ScriptLifecycle.Failed, instance.Lifecycle);
        Assert.Empty(session.NativeSinks.Traces);
        Assert.Equal(VmStopReason.ModelError, session.Host.Initialize(instance).Reason);
        Assert.Empty(session.NativeSinks.Traces);
        Assert.Equal(0, ((ScriptHost)session.Host).NativeTargetRefusals);
    }

    [Theory]
    [InlineData("Trace", "probe")]
    [InlineData("Notification", "notification: probe")]
    [InlineData("MessageBox", "messagebox: probe")]
    public void CacheLoadedSessionReportsItsAllowedDebugCalls(string function, string expected)
    {
        using var cache = new TempCache();
        var session = Load(cache);
        var value = ((INativeTarget)session.Host).Invoke(new NativeId("Debug", function), NativeTargetKind.Static,
            PapyrusValue.None, new[] { PapyrusValue.FromString("probe") });

        Assert.Equal(PapyrusValue.None, value);
        var sinks = session.NativeSinks;
        Assert.Equal(expected, Assert.Single(sinks.Traces));
        Assert.Equal(3, sinks.Count);
        Assert.Equal(0, ((ScriptHost)session.Host).NativeTargetRefusals);
        Assert.Empty(session.Host.Instances);
    }

    [Fact]
    public void LoadingBaseSinksDoesNotFabricateOtherNativeBindings()
    {
        using var cache = new TempCache();
        var session = Load(cache);
        Assert.Throws<InvalidOperationException>(() => ((INativeTarget)session.Host).Invoke(
            new NativeId("Game", "GetPlayer"), NativeTargetKind.Static, PapyrusValue.None, []));
        Assert.Equal(1, ((ScriptHost)session.Host).NativeTargetRefusals);
    }
}
