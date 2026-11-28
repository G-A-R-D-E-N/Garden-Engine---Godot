using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class XpNativeAdaptersTests : HostFixture
{
    private IScriptHost Host(HostKind kind, EvidenceGate gate, string body) => Track(TestHosts.Create(kind, gate,
        TestHosts.Stub("Game", null,
            "Function RewardPlayerXP(int auiXPAmount, bool abDirect = false) native global\n" +
            "int Function GetXPForLevel(int auiLevel) native global"),
        new ScriptSpec("Caller", $"Scriptname Caller\nInt Function Run()\n  {body}\nEndFunction\n")));

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    // Regression: a script XP reward must reach the experience store with its amount and direct flag, and the level total must come from the curve, or quest rewards never raise the Pip-Boy level.
    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void RewardPlayerXPReachesTheStoreAndGetXPForLevelAnswersFromTheCurve(HostKind kind)
    {
        var rewards = new List<(long Amount, bool Direct)>();
        var gate = TestHosts.Gate((XpNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, "Game.RewardPlayerXP(40)\n  Game.RewardPlayerXP(25, True)\n  Return Game.GetXPForLevel(3)");
        var report = new XpNativeAdapters(host, gate, (amount, direct) => rewards.Add((amount, direct)), level => level * 100L).Bind();

        var result = Run(host);

        Assert.Equal(2, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(300, result.Value.Integer);
        Assert.Equal(new[] { (40L, false), (25L, true) }, rewards);
    }

    // Regression: the recorded registry must gate the XP system, or Bind throws at runtime when the row is missing.
    [Fact]
    public void TheRecordedRegistryApproximatesTheXpSystem()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));

        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(XpNativeAdapters.SystemKey));
    }
}
