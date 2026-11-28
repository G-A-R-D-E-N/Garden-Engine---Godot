using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class TeammateNativeAdaptersTests : HostFixture
{
    private static readonly PapyrusOwnerId Companion = PapyrusOwnerId.ForPlacedReference("000800:Base.esp", "000A00");

    private static PapyrusValue Ref(PapyrusOwnerId owner) => PapyrusValue.FromObject(new EngineObjectReference(owner));

    private IScriptHost Host(HostKind kind, EvidenceGate gate, PapyrusValue receiver, string body)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Actor", null,
                "Function SetPlayerTeammate(bool abTeammate = true, bool abCanDoFavor=true, bool abGivePlayerXP=false) native\n" +
                "bool Function IsPlayerTeammate() native"),
            TestHosts.Stub("ReceiverSource", null, "Actor Function Target() global native"),
            new ScriptSpec("Caller",
                "Scriptname Caller\nBool Function Run()\n  Actor target = ReceiverSource.Target()\n" +
                $"  {body}\n  Return target.IsPlayerTeammate()\nEndFunction\n")));
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
    public void SetPlayerTeammateDefaultsToTrueAndWritesTheStateKeyedByTheReference(HostKind kind)
    {
        var teammates = new HashSet<string>();
        var gate = TestHosts.Gate((TeammateNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Ref(Companion), "target.SetPlayerTeammate()");
        var report = new TeammateNativeAdapters(host, gate, (key, on) => { if (on) teammates.Add(key); else teammates.Remove(key); }, teammates.Contains).Bind();

        var result = Run(host);

        Assert.Equal(2, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.True(result.Value.Flag);
        Assert.Contains("000A00", teammates);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void SetPlayerTeammateFalseClearsItAndTheOtherArgumentsAreIgnored(HostKind kind)
    {
        var teammates = new HashSet<string>();
        var gate = TestHosts.Gate((TeammateNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Ref(Companion), "target.SetPlayerTeammate(True, False, True)\n  target.SetPlayerTeammate(False, True)");
        new TeammateNativeAdapters(host, gate, (key, on) => { if (on) teammates.Add(key); else teammates.Remove(key); }, teammates.Contains).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.False(result.Value.Flag);
        Assert.Empty(teammates);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AScriptPropertyHoldingTheBareReferenceKeyIsTheSameTarget(HostKind kind)
    {
        var teammates = new HashSet<string>();
        var gate = TestHosts.Gate((TeammateNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, PapyrusValue.FromObject(new EngineObjectReference("000A00:Base.esp")), "target.SetPlayerTeammate()");
        new TeammateNativeAdapters(host, gate, (key, on) => { if (on) teammates.Add(key); }, teammates.Contains).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { "000A00:Base.esp" }, teammates.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnIdentityThatIsNotAReferenceKeyIsRefusedAndWritesNothing(HostKind kind)
    {
        var teammates = new HashSet<string>();
        var gate = TestHosts.Gate((TeammateNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, PapyrusValue.FromObject(new EngineObjectReference("no-colon")), "target.SetPlayerTeammate()");
        new TeammateNativeAdapters(host, gate, (key, on) => { if (on) teammates.Add(key); }, teammates.Contains).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(teammates);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnsupportedReceiverIsNotATeammate(HostKind kind)
    {
        var gate = TestHosts.Gate((TeammateNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, PapyrusValue.FromObject(new EngineObjectReference("no-colon")), "");
        new TeammateNativeAdapters(host, gate, (_, _) => { }, _ => true).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.False(result.Value.Flag);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsTheNatives(HostKind kind)
    {
        var gate = TestHosts.Gate((TeammateNativeAdapters.SystemKey, "UNPROVEN"));
        var host = Host(kind, gate, Ref(Companion), "target.SetPlayerTeammate()");

        var report = new TeammateNativeAdapters(host, gate, (_, _) => { }, _ => false).Bind();

        Assert.Equal(2, report.Bound);
        Assert.All(report.Natives, outcome => Assert.Equal(AdapterState.Bound, outcome.State));
    }

    [Fact]
    public void TheRecordedRegistryApproximatesTheTeammateSystemSoTheGateAllowsAndCountsOneApproximation()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));
        var gate = new EvidenceGate(registry);

        var decision = gate.Gate(TeammateNativeAdapters.SystemKey);

        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(TeammateNativeAdapters.SystemKey));
        Assert.True(decision.Allowed);
        Assert.Equal(1, gate.ApproximationCount);
        Assert.Equal(0, gate.StopCount);
    }

    // Regression: abGivePlayerXP must reach the XP share (true only while the teammate flag is set), or companion kills never award the player XP.
    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheGivePlayerXpArgumentMarksTheTeammateAndIsDroppedWithTheFlag(HostKind kind)
    {
        var shares = new List<bool>();
        var gate = TestHosts.Gate((TeammateNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, Ref(Companion), "target.SetPlayerTeammate(True, False, True)\n  target.SetPlayerTeammate(True)\n  target.SetPlayerTeammate(False, True, True)");
        new TeammateNativeAdapters(host, gate, (_, _) => { }, _ => false, (_, gives) => shares.Add(gives)).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { true, false, false }, shares);
    }
}
