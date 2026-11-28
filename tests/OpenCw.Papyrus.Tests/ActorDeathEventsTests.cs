using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ActorDeathEventsTests : HostFixture
{
    private const string Base = "000800:Npcs.esp";

    private static readonly PapyrusOwnerId Dead = PapyrusOwnerId.ForPlacedReference(Base, "000A00");
    private static readonly PapyrusOwnerId Killer = PapyrusOwnerId.ForPlacedReference(Base, "000B00");

    private static EvidenceGate Gate(string dying = "APPROXIMATED", string death = "APPROXIMATED") => TestHosts.Gate(
        (ActorEventFamilies.DirectKey, "ENGINE_POLICY"),
        (PapyrusEventBridge.KeyPrefix + ActorDeathEvents.Dying, dying),
        (PapyrusEventBridge.KeyPrefix + ActorDeathEvents.Death, death));

    private (ActorDeathEvents Deaths, List<string> Seen) Setup(HostKind kind, EvidenceGate gate)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Actor"),
            new ScriptSpec("DeathScript",
                "Scriptname DeathScript extends Actor\n" +
                "Function Report(String tag, Actor killer) global native\n" +
                "Event OnDying(Actor akKiller)\n  Report(\"dying\", akKiller)\nEndEvent\n" +
                "Event OnDeath(Actor akKiller)\n  Report(\"death\", akKiller)\nEndEvent\n")));
        var seen = new List<string>();
        host.RegisterNative(new NativeId("DeathScript", "Report"), arguments =>
        {
            seen.Add($"{arguments[0]}:{(arguments[1].Reference is EngineObjectReference reference ? reference.Identity : "None")}");
            return PapyrusValue.None;
        });
        var bridge = new PapyrusEventBridge(host, gate);
        var instance = host.Attach("DeathScript");
        host.Initialize(instance);
        bridge.Register(Dead, instance);
        return (new ActorDeathEvents(new ActorEventFamilies(bridge, new ScriptRegistrationRegistry(), gate)), seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void DyingThenDeathAreDeliveredToTheOwnerWithTheKiller(HostKind kind)
    {
        var (deaths, seen) = Setup(kind, Gate());

        var reports = deaths.Died(Dead, Killer);

        Assert.All(reports, report => Assert.Equal(1, report.Delivered));
        Assert.Equal(new[] { $"dying:{Killer.Key}", $"death:{Killer.Key}" }, seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANullKillerIsPassedAsNone(HostKind kind)
    {
        var (deaths, seen) = Setup(kind, Gate());

        deaths.Died(Dead, null);

        Assert.Equal(new[] { "dying:None", "death:None" }, seen);
    }

}
