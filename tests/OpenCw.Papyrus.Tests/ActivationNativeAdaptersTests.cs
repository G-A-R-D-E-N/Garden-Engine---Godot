using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ActivationNativeAdaptersTests : HostFixture
{
    private sealed class Blocks
    {
        private readonly Dictionary<string, bool> _hide = new();

        public void Set(string key, bool blocked, bool hide)
        {
            if (blocked) _hide[key] = hide;
            else _hide.Remove(key);
        }

        public bool IsBlocked(string key) => _hide.ContainsKey(key);

        public bool HidesText(string key) => _hide.TryGetValue(key, out var hide) && hide;
    }

    private static readonly PapyrusOwnerId Door = PapyrusOwnerId.ForPlacedReference("000800:Base.esp", "000A00");

    private static PapyrusValue Ref(PapyrusOwnerId owner) => PapyrusValue.FromObject(new EngineObjectReference(owner));

    private IScriptHost Host(HostKind kind, EvidenceGate gate, PapyrusValue receiver, string body)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("ObjectReference", null,
                "Function BlockActivation(bool abBlocked = true, bool abHideActivateText = false) native\n" +
                "bool Function IsActivationBlocked() native"),
            TestHosts.Stub("ReceiverSource", null, "ObjectReference Function Target() global native"),
            new ScriptSpec("Caller",
                "Scriptname Caller\nBool Function Run()\n  ObjectReference target = ReceiverSource.Target()\n" +
                $"  {body}\n  Return target.IsActivationBlocked()\nEndFunction\n")));
        host.RegisterNative(new NativeId("ReceiverSource", "Target"), _ => receiver);
        return host;
    }

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void BlockActivationWritesTheSharedStateKeyedByTheReference(HostKind kind)
    {
        var blocks = new Blocks();
        var gate = TestHosts.Gate((ActivationNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = Host(kind, gate, Ref(Door), "target.BlockActivation(True, True)");
        var report = new ActivationNativeAdapters(host, gate, blocks.Set, blocks.IsBlocked).Bind();

        var result = Run(host);

        Assert.Equal(2, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.True(result.Value.Flag);
        Assert.True(blocks.IsBlocked("000A00"));
        Assert.True(blocks.HidesText("000A00"));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void BlockActivationDefaultsToBlockedAndFalseClearsIt(HostKind kind)
    {
        var blocks = new Blocks();
        var gate = TestHosts.Gate((ActivationNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = Host(kind, gate, Ref(Door), "target.BlockActivation()\n  target.BlockActivation(False)");
        new ActivationNativeAdapters(host, gate, blocks.Set, blocks.IsBlocked).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.False(result.Value.Flag);
        Assert.False(blocks.IsBlocked("000A00"));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsTheNatives(HostKind kind)
    {
        var gate = TestHosts.Gate((ActivationNativeAdapters.SystemKey, "UNPROVEN"));
        var host = Host(kind, gate, Ref(Door), "target.BlockActivation()");

        var report = new ActivationNativeAdapters(host, gate, (_, _, _) => { }, _ => false).Bind();

        Assert.Equal(2, report.Bound);
        Assert.All(report.Natives, outcome => Assert.Equal(AdapterState.Bound, outcome.State));
    }

    [Fact]
    public void TheRecordedRegistryApproximatesTheActivationSystemSoTheGateAllowsAndCountsOneApproximation()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));
        var gate = new EvidenceGate(registry);

        var decision = gate.Gate(ActivationNativeAdapters.SystemKey);

        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(ActivationNativeAdapters.SystemKey));
        Assert.Equal(GateOutcome.Approximated, decision.Outcome);
        Assert.True(decision.Allowed);
        Assert.Equal(1, gate.ApproximationCount);
        Assert.Equal(0, gate.StopCount);
    }
}
