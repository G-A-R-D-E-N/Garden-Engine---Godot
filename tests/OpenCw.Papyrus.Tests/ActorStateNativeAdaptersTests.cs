using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ActorStateNativeAdaptersTests : HostFixture
{
    private static readonly PapyrusOwnerId Actor = PapyrusOwnerId.ForPlacedReference("000800:Base.esp", "000A00");

    private sealed class FakeAccess : IActorStateAccess
    {
        public readonly Dictionary<(string, ActorStateFlag), bool> Flags = new();
        public readonly Dictionary<(string, ActorStateQuery), bool> Answers = new();

        public bool TrySet(string refKey, ActorStateFlag flag, bool value)
        {
            Flags[(refKey, flag)] = value;
            return true;
        }

        public bool TryGet(string refKey, ActorStateQuery query, out bool value) => Answers.TryGetValue((refKey, query), out value);
    }

    private static PapyrusValue Ref(PapyrusOwnerId owner) => PapyrusValue.FromObject(new EngineObjectReference(owner));

    private IScriptHost Host(HostKind kind, EvidenceGate gate, PapyrusValue receiver, string body, string result)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Actor", null,
                "Function AllowBleedoutDialogue(bool abCanTalk) native\n" +
                "Function SetNoBleedoutRecovery(bool abAllowed) native\n" +
                "bool Function GetNoBleedoutRecovery() native\n" +
                "bool Function IsBleedingOut() native\n" +
                "bool Function IsBeingRidden() native\n" +
                "bool Function IsOnMount() native"),
            TestHosts.Stub("ReceiverSource", null, "Actor Function Target() global native"),
            new ScriptSpec("Caller",
                "Scriptname Caller\nBool Function Run()\n  Actor target = ReceiverSource.Target()\n" +
                $"  {body}\n  Return {result}\nEndFunction\n")));
        host.RegisterNative(new NativeId("ReceiverSource", "Target"), _ => receiver);
        return host;
    }

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    private static EvidenceGate Approximated() => TestHosts.Gate((ActorStateNativeAdapters.SystemKey, "APPROXIMATED"));

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheWritesReachTheStateKeyedByTheReference(HostKind kind)
    {
        var access = new FakeAccess();
        var gate = Approximated();
        var host = Host(kind, gate, Ref(Actor), "target.AllowBleedoutDialogue(True)\n  target.SetNoBleedoutRecovery(True)\n  target.SetNoBleedoutRecovery(False)", "True");
        var report = new ActorStateNativeAdapters(host, gate, access).Bind();

        var result = Run(host);

        Assert.Equal(6, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.True(access.Flags[("000A00", ActorStateFlag.CanSpeakWhenDown)]);
        Assert.False(access.Flags[("000A00", ActorStateFlag.NoBleedoutRecovery)]);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void EachGetterReadsItsQuery(HostKind kind)
    {
        foreach (var (name, asked) in new[]
        {
            ("IsBleedingOut", ActorStateQuery.BleedingOut), ("IsBeingRidden", ActorStateQuery.BeingRidden),
            ("IsOnMount", ActorStateQuery.OnMount), ("GetNoBleedoutRecovery", ActorStateQuery.NoBleedoutRecovery),
        })
        {
            foreach (var answer in new[] { true, false })
            {
                var access = new FakeAccess();
                access.Answers[("000A00", asked)] = answer;
                var gate = Approximated();
                var host = Host(kind, gate, Ref(Actor), "", $"target.{name}()");
                new ActorStateNativeAdapters(host, gate, access).Bind();

                var result = Run(host);

                Assert.True(result.Completed, TestHosts.Describe(host, result));
                Assert.Equal(answer, result.Value.Flag);
            }
        }
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnsupportedReceiverReadsFalseAndIgnoresWritesWithoutThrowing(HostKind kind)
    {
        var access = new FakeAccess();
        var gate = Approximated();
        var host = Host(kind, gate, PapyrusValue.FromObject(new EngineObjectReference("no-colon")),
            "target.AllowBleedoutDialogue(True)\n  target.SetNoBleedoutRecovery(True)", "target.IsBleedingOut() || target.IsBeingRidden() || target.IsOnMount() || target.GetNoBleedoutRecovery()");
        new ActorStateNativeAdapters(host, gate, access).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.False(result.Value.Flag);
        Assert.Empty(access.Flags);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnActorWithNoStateReadsFalse(HostKind kind)
    {
        var gate = Approximated();
        var host = Host(kind, gate, Ref(Actor), "", "target.IsBleedingOut()");
        new ActorStateNativeAdapters(host, gate, new FakeAccess()).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.False(result.Value.Flag);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsTheNatives(HostKind kind)
    {
        var unproven = TestHosts.Gate((ActorStateNativeAdapters.SystemKey, "UNPROVEN"));
        var report = new ActorStateNativeAdapters(Host(kind, unproven, Ref(Actor), "", "True"), unproven, new FakeAccess()).Bind();
        Assert.Equal(6, report.Bound);
    }

    [Fact]
    public void TheRecordedRegistryApproximatesTheActorStateSystem()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));
        var gate = new EvidenceGate(registry);

        var decision = gate.Gate(ActorStateNativeAdapters.SystemKey);

        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(ActorStateNativeAdapters.SystemKey));
        Assert.True(decision.Allowed);
    }
}
