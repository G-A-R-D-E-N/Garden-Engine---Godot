using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ActorHitEventsTests : HostFixture
{
    private static readonly PapyrusOwnerId Victim = PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000A00");
    private static readonly PapyrusOwnerId Attacker = PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000A01");
    private static readonly PapyrusOwnerId Bystander = PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000A02");
    private static readonly PapyrusOwnerId Watcher = PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000B00");

    private const string OnHitHandler =
        "Event OnHit(ObjectReference akTarget, ObjectReference akAggressor, Form akSource, Projectile akProjectile, bool abPowerAttack, bool abSneakAttack, bool abBashAttack, bool abHitBlocked, string apMaterial)\n";

    private sealed class Rig
    {
        public required IScriptHost Host { get; init; }
        public required ActorHitEvents Hits { get; init; }
        public required ScriptInstance Watching { get; init; }
        public required List<string> Seen { get; init; }
        public required AdapterReport Report { get; init; }

        public void Run(string function, params PapyrusValue[] arguments)
        {
            var result = Host.RunEvent(Watching, function, arguments);
            Assert.True(result.Completed, TestHosts.Describe(Host, result));
        }
    }

    private static PapyrusValue Ref(PapyrusOwnerId owner) => PapyrusValue.FromObject(new EngineObjectReference(owner));

    private Rig Setup(HostKind kind)
    {
        var gate = TestHosts.Gate((PapyrusEventBridge.KeyPrefix + ActorHitEvents.Hit, "ENGINE_POLICY"));
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("ScriptObject", null,
                "Function RegisterForHitEvent(ScriptObject akTarget, ScriptObject akAggressorFilter, Form akSourceFilter, Form akProjectileFilter, int aiPowerFilter, int aiSneakFilter, int aiBashFilter, int aiBlockFilter, bool abMatch) native",
                "Function UnregisterForHitEvent(ScriptObject akTarget, ScriptObject akAggressorFilter, Form akSourceFilter, Form akProjectileFilter, int aiPowerFilter, int aiSneakFilter, int aiBashFilter, int aiBlockFilter, bool abMatch) native",
                "Function UnregisterForAllHitEvents(ScriptObject akTarget) native"),
            TestHosts.Stub("Form"), TestHosts.Stub("ObjectReference", "ScriptObject"), TestHosts.Stub("Projectile"),
            TestHosts.Stub("Probe", null, "Function Report(string who, ObjectReference target, ObjectReference aggressor, bool power) global native"),
            new ScriptSpec("Watching",
                "Scriptname Watching extends ObjectReference\n" +
                "Function Watch(ObjectReference akTarget, ObjectReference akFrom, int aiPower, bool abMatch)\n  RegisterForHitEvent(akTarget, akFrom, None, None, aiPower, -1, -1, -1, abMatch)\nEndFunction\n" +
                "Function Unwatch(ObjectReference akTarget, ObjectReference akFrom)\n  UnregisterForHitEvent(akTarget, akFrom, None, None, -1, -1, -1, -1, true)\nEndFunction\n" +
                "Function Drop(ObjectReference akTarget)\n  UnregisterForAllHitEvents(akTarget)\nEndFunction\n" +
                OnHitHandler + "  Probe.Report(\"heard\", akTarget, akAggressor, abPowerAttack)\nEndEvent\n"),
            new ScriptSpec("Deaf",
                "Scriptname Deaf extends ObjectReference\n" + OnHitHandler + "  Probe.Report(\"deaf\", akTarget, akAggressor, abPowerAttack)\nEndEvent\n")));
        var seen = new List<string>();
        host.RegisterNative(new NativeId("Probe", "Report"), arguments =>
        {
            string Name(PapyrusValue value) => value.Reference is EngineObjectReference reference ? reference.Identity : "None";
            seen.Add($"{arguments[0].Text}|{Name(arguments[1])}|{Name(arguments[2])}|{arguments[3].Flag}");
            return PapyrusValue.None;
        });
        var table = new HitRegistrations();
        var report = new HitEventNativeAdapters(host, gate, table).Bind();
        var watching = host.Attach("Watching", owner: Watcher);
        Assert.True(host.Initialize(watching).Completed);
        var deaf = host.Attach("Deaf", owner: Victim);
        Assert.True(host.Initialize(deaf).Completed);
        return new Rig { Host = host, Hits = new ActorHitEvents(table, host, gate), Watching = watching, Seen = seen, Report = report };
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ARegisteredScriptHearsAHitOnItsTargetWithTheTargetAndTheAggressor(HostKind kind)
    {
        var rig = Setup(kind);
        rig.Run("Watch", Ref(Victim), PapyrusValue.None, PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));

        Assert.Equal(3, rig.Report.Bound);
        Assert.Equal(1, rig.Hits.Struck(Victim, Attacker));
        Assert.Equal(new[] { $"heard|{Victim.Key}|{Attacker.Key}|False" }, rig.Seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AHitWithNoKnownAggressorDeliversNoneForItAndAScriptThatNeverRegisteredHearsNothing(HostKind kind)
    {
        var rig = Setup(kind);
        Assert.Equal(0, rig.Hits.Struck(Victim, Attacker));
        rig.Run("Watch", Ref(Victim), PapyrusValue.None, PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));

        Assert.Equal(1, rig.Hits.Struck(Victim, null));

        Assert.Equal(new[] { $"heard|{Victim.Key}|None|False" }, rig.Seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AHitOnAnotherActorIsNotDeliveredToARegistrationForTheTarget(HostKind kind)
    {
        var rig = Setup(kind);
        rig.Run("Watch", Ref(Victim), PapyrusValue.None, PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));

        Assert.Equal(0, rig.Hits.Struck(Bystander, Attacker));
        Assert.Empty(rig.Seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnAggressorFilterDeliversOnlyHitsFromThatActor(HostKind kind)
    {
        var rig = Setup(kind);
        rig.Run("Watch", Ref(Victim), Ref(Attacker), PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));

        Assert.Equal(0, rig.Hits.Struck(Victim, Bystander));
        Assert.Equal(0, rig.Hits.Struck(Victim, null));
        Assert.Equal(1, rig.Hits.Struck(Victim, Attacker));
        Assert.Equal(new[] { $"heard|{Victim.Key}|{Attacker.Key}|False" }, rig.Seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AFlagFilterOfOneNeverMatchesBecauseGardenReportsNoPowerAttacksAndZeroDoes(HostKind kind)
    {
        var rig = Setup(kind);
        rig.Run("Watch", Ref(Victim), PapyrusValue.None, PapyrusValue.FromInt(1), PapyrusValue.FromBool(true));
        Assert.Equal(0, rig.Hits.Struck(Victim, Attacker));

        rig.Run("Watch", Ref(Victim), PapyrusValue.None, PapyrusValue.FromInt(0), PapyrusValue.FromBool(true));

        Assert.Equal(1, rig.Hits.Struck(Victim, Attacker));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AMatchFlagOfFalseDeliversTheHitsThatMissTheFilter(HostKind kind)
    {
        var rig = Setup(kind);
        rig.Run("Watch", Ref(Victim), Ref(Attacker), PapyrusValue.FromInt(-1), PapyrusValue.FromBool(false));

        Assert.Equal(0, rig.Hits.Struck(Victim, Attacker));
        Assert.Equal(1, rig.Hits.Struck(Victim, Bystander));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void RegisteringTheSameFilterTwiceIsOneRegistrationAndUnregisteringItEndsIt(HostKind kind)
    {
        var rig = Setup(kind);
        rig.Run("Watch", Ref(Victim), PapyrusValue.None, PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));
        rig.Run("Watch", Ref(Victim), PapyrusValue.None, PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));
        Assert.Equal(1, rig.Hits.Registered);
        Assert.Equal(1, rig.Hits.Struck(Victim, Attacker));

        rig.Run("Unwatch", Ref(Victim), PapyrusValue.None);

        Assert.Equal(0, rig.Hits.Registered);
        Assert.Equal(0, rig.Hits.Struck(Victim, Attacker));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void UnregisterForAllHitEventsDropsEveryRegistrationOnThatTargetAndKeepsTheOthers(HostKind kind)
    {
        var rig = Setup(kind);
        rig.Run("Watch", Ref(Victim), PapyrusValue.None, PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));
        rig.Run("Watch", Ref(Victim), Ref(Attacker), PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));
        rig.Run("Watch", Ref(Bystander), PapyrusValue.None, PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));

        rig.Run("Drop", Ref(Victim));

        Assert.Equal(1, rig.Hits.Registered);
        Assert.Equal(0, rig.Hits.Struck(Victim, Attacker));
        Assert.Equal(1, rig.Hits.Struck(Bystander, Attacker));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void UnregisterForAllHitEventsWithNoTargetDropsEveryRegistrationOfTheScript(HostKind kind)
    {
        var rig = Setup(kind);
        rig.Run("Watch", Ref(Victim), PapyrusValue.None, PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));
        rig.Run("Watch", Ref(Bystander), PapyrusValue.None, PapyrusValue.FromInt(-1), PapyrusValue.FromBool(true));

        rig.Run("Drop", PapyrusValue.None);

        Assert.Equal(0, rig.Hits.Registered);
    }

    [Fact]
    public void OnHitIsListedAsAnEmittedName()
    {
        Assert.Contains(PapyrusEventContract.Emitters, emitter => emitter.Name == ActorHitEvents.Hit);
    }
}
