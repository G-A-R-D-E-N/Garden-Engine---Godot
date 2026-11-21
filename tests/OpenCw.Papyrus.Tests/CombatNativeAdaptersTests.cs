using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class CombatNativeAdaptersTests : HostFixture
{
    private static readonly PapyrusOwnerId Npc = PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000A00");
    private static readonly NativeId IsDead = new("Actor", "IsDead");

    private sealed class FakeLife : IActorLifeSource
    {
        public Dictionary<PapyrusOwnerId, bool> Dead { get; } = new();

        public bool TryIsDead(PapyrusOwnerId owner, out bool dead) => Dead.TryGetValue(owner, out dead);
    }

    private static EvidenceGate Gate(string status) => TestHosts.Gate((CombatNativeAdapters.SystemKey, status));

    private static PapyrusValue Ref(PapyrusOwnerId owner) => PapyrusValue.FromObject(new EngineObjectReference(owner));

    private IScriptHost Host(HostKind kind, EvidenceGate gate, PapyrusValue receiver, string parameters = "", string arguments = "")
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Actor", null, $"Bool Function IsDead({parameters}) native"),
            TestHosts.Stub("ReceiverSource", null, "Actor Function Target() global native"),
            new ScriptSpec("Caller",
                "Scriptname Caller\nBool Function Run()\n  Actor target = ReceiverSource.Target()\n" +
                $"  Return target.IsDead({arguments})\nEndFunction\n")));
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
    public void IsDeadReadsTheReceiversLifeFromTheGardenSource(HostKind kind)
    {
        var life = new FakeLife();
        life.Dead[Npc] = true;
        var gate = Gate("ENGINE_POLICY");
        var host = Host(kind, gate, Ref(Npc));
        var report = new CombatNativeAdapters(host, gate, life).Bind();

        var result = Run(host);

        Assert.Equal(AdapterState.Bound, Assert.Single(report.Natives).State);
        Assert.Equal(1, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(PapyrusValueKind.Bool, result.Value.Kind);
        Assert.True(result.Value.Flag);

        life.Dead[Npc] = false;
        Assert.False(Run(host).Value.Flag);
    }


    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnExistingIsDeadHandlerIsPreserved(HostKind kind)
    {
        var gate = Gate("ENGINE_POLICY");
        var host = Host(kind, gate, Ref(Npc));
        host.RegisterNative(IsDead, (_, _) => PapyrusValue.FromBool(true), NativeTargetKind.EngineObject);

        var report = new CombatNativeAdapters(host, gate, new FakeLife()).Bind();

        Assert.Equal(AdapterState.Conflict, Assert.Single(report.Natives).State);
        Assert.True(Run(host).Value.Flag);
    }

    [Theory]
    [MemberData(nameof(RefusalCases))]
    public void IsDeadRefusesWhatGardenCannotAnswer(HostKind kind, string name, string reason)
    {
        var (receiver, parameters, arguments) = name switch
        {
            "unknown-actor" => (Ref(PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000B00")), "", ""),
            "ownerless" => (PapyrusValue.FromObject(new EngineObjectReference("loose")), "", ""),
            "form-owner" => (Ref(PapyrusOwnerId.ForForm("000800:Npcs.esp")), "", ""),
            _ => (Ref(Npc), "Int aiExtra", "1"),
        };
        var life = new FakeLife();
        life.Dead[Npc] = true;
        var gate = Gate("ENGINE_POLICY");
        var host = Host(kind, gate, receiver, parameters, arguments);
        new CombatNativeAdapters(host, gate, life).Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Contains(reason, ((RadicleScriptHost)host).LastNativeFailure ?? string.Empty, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> RefusalCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            yield return new[] { host[0], "unknown-actor", "no actor state" };
            yield return new[] { host[0], "ownerless", "valid Garden owner" };
            yield return new[] { host[0], "form-owner", "owner kind" };
            yield return new[] { host[0], "extra", "extra argument" };
        }
    }

}
