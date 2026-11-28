using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class CoreNativeAdaptersTests : HostFixture
{
    private IScriptHost Host(HostKind kind, EvidenceGate gate, string body) => Track(TestHosts.Create(kind, gate,
        TestHosts.Stub("Math", null, "float Function abs(float afValue) native global", "float Function sqrt(float afValue) native global", "float Function pow(float afValue1, float afValue2) native global", "Int Function floor(float afValue) native global", "float Function sin(float afValue) native global"),
        TestHosts.Stub("Utility", null, "float Function RandomFloat(float afMin = 0.0, float afMax = 1.0) native global\nfloat Function GetCurrentGameTime() native global"),
        TestHosts.Stub("Game", null, "Function Warning(string asText) native global\nFunction Error(string asText) native global"),
        new ScriptSpec("Caller", $"Scriptname Caller\nFloat Function Run()\n  {body}\nEndFunction\n")));

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AbsRandomClockAndLogAnswerFromTheirSources(HostKind kind)
    {
        var logged = new List<string>();
        var gate = TestHosts.Gate((CoreNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = Host(kind, gate, "Game.Warning(\"careful\")\n  Return Math.abs(-2.5) + Utility.RandomFloat(10.0, 20.0) + Utility.GetCurrentGameTime()");
        var report = new CoreNativeAdapters(host, gate, () => 0.5, () => 3.0, () => "000007:Fallout4.esm", logged.Add).Bind();

        var result = Run(host);

        Assert.Equal(19, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(2.5 + 15.0 + 3.0, result.Value.Real, 4);
        Assert.Equal(new[] { "warning: careful" }, logged);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void MathNativesAnswerAndANonNumberStopsByName(HostKind kind)
    {
        var gate = TestHosts.Gate((CoreNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = Host(kind, gate, "Return Math.sqrt(16.0) + Math.pow(2.0, 3.0) + Math.floor(2.9) + Math.sin(90.0)");
        new CoreNativeAdapters(host, gate, () => 0.5, () => 3.0, () => null, _ => { }).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(4.0 + 8.0 + 2.0 + 1.0, result.Value.Real, 4);

        var negative = Host(kind, gate, "Return Math.sqrt(-1.0)");
        new CoreNativeAdapters(negative, gate, () => 0.5, () => 3.0, () => null, _ => { }).Bind();
        Assert.False(Run(negative).Completed);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void NoClockStopsTheScriptInsteadOfAnsweringZero(HostKind kind)
    {
        var gate = TestHosts.Gate((CoreNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = Host(kind, gate, "Return Utility.GetCurrentGameTime()");
        new CoreNativeAdapters(host, gate, () => 0.5, () => null, () => null, _ => { }).Bind();

        var result = Run(host);

        Assert.False(result.Completed);
    }

    [Fact]
    public void TheRecordedRegistryHoldsTheCoreAndUnsupportedRows()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));

        Assert.Equal(EvidenceStatus.EnginePolicy, registry.StatusFor(CoreNativeAdapters.SystemKey));
        Assert.Equal(EvidenceStatus.EnginePolicy, registry.StatusFor(UnsupportedNativeAdapters.SystemKey));
    }
}
