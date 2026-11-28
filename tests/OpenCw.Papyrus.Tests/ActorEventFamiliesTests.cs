using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ActorEventFamiliesTests : HostFixture
{
    private const string Base = "000800:Npcs.esp";

    private static readonly (string Key, string Status)[] ExtraRows =
    {
        (ActorEventFamilies.DirectKey, "PROVEN"),
        (ActorEventFamilies.RegisteredKey, "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnDeath", "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnCombatStateChanged", "PROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides) =>
        TestHosts.Gate(ExtraRows.Concat(overrides).ToArray());

    private static PapyrusOwnerId Owner(string reference) => PapyrusOwnerId.ForPlacedReference(Base, reference);

    private (ActorEventFamilies Families, PapyrusEventBridge Bridge,
        ScriptRegistrationRegistry Registrations, List<string> Seen) Setup(HostKind kind, EvidenceGate gate)
    {
        var host = Track(TestHosts.Create(kind, gate, TestHosts.Stub("ActorScript", null,
            "Function OnInit()\nEndFunction",
            "Function OnDeath()\nEndFunction",
            "Function OnCombatStateChanged()\nEndFunction")));
        var seen = new List<string>();
        var overrides = new ScriptOverrideRegistry(gate);
        host.Overrides = overrides;
        foreach (var name in new[] { "OnDeath", "OnCombatStateChanged" })
        {
            overrides.Register("ActorScript", name, ScriptOverrideRegistry.AnyParameterCount, context =>
            {
                seen.Add(context.Function);
                return OverrideResult.Handled(PapyrusValue.None);
            });
        }

        var bridge = new PapyrusEventBridge(host, gate);
        var registrations = new ScriptRegistrationRegistry();
        var instance = host.Attach("ActorScript");
        host.Initialize(instance);
        bridge.Register(Owner("000A00"), instance);
        return (new ActorEventFamilies(bridge, registrations, gate), bridge, registrations, seen);
    }

    private static ActorEvent Direct(string name, string? filter = null) =>
        new(name, ActorEventPath.DirectOwner, filter, Array.Empty<PapyrusValue>());

    private static ActorEvent Registered(string name, string? filter = null) =>
        new(name, ActorEventPath.RegistrationRequired, filter, Array.Empty<PapyrusValue>());

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ADirectOwnerEventIsDeliveredWithoutARegistration(HostKind kind)
    {
        var gate = Gate();
        var (families, bridge, _, seen) = Setup(kind, gate);

        var report = families.Deliver(Owner("000A00"), Direct("OnDeath"));

        Assert.Equal(1, report.Delivered);
        Assert.Equal(new[] { "OnDeath" }, seen);
        Assert.Equal(1, bridge.Delivered);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ARegistrationRequiredEventWithoutARegistrationIsRefused(HostKind kind)
    {
        var gate = Gate();
        var (families, bridge, _, seen) = Setup(kind, gate);

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged"));

        Assert.Equal(0, report.Delivered);
        Assert.Equal(1, report.Refused);
        Assert.Empty(seen);
        Assert.Equal(0, bridge.Delivered);
        Assert.Contains("did not register", report.Outcomes[0].Reason, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ARegisteredOwnerReceivesTheEvent(HostKind kind)
    {
        var gate = Gate();
        var (families, bridge, registrations, seen) = Setup(kind, gate);
        registrations.Register(Owner("000A00"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged"));

        Assert.Equal(1, report.Delivered);
        Assert.Equal(new[] { "OnCombatStateChanged" }, seen);
        Assert.Equal(1, bridge.Delivered);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ANonMatchingFilterIsFilteredOut(HostKind kind)
    {
        var gate = Gate();
        var (families, _, registrations, seen) = Setup(kind, gate);
        registrations.Register(Owner("000A00"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));
        families.SetFilter(Owner("000A00"), "OnCombatStateChanged", "combat");

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged", "peace"));

        Assert.Equal(0, report.Delivered);
        Assert.Equal(1, report.Filtered);
        Assert.Equal(ActorEventState.FilteredOut, report.Outcomes[0].State);
        Assert.Empty(seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AMatchingFilterIsDelivered(HostKind kind)
    {
        var gate = Gate();
        var (families, _, registrations, seen) = Setup(kind, gate);
        registrations.Register(Owner("000A00"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));
        families.SetFilter(Owner("000A00"), "OnCombatStateChanged", "combat");

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged", "combat"));

        Assert.Equal(1, report.Delivered);
        Assert.Equal(new[] { "OnCombatStateChanged" }, seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnotherOwnersRegistrationDoesNotDeliverHere(HostKind kind)
    {
        var gate = Gate();
        var (families, _, registrations, _) = Setup(kind, gate);
        registrations.Register(Owner("000A01"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged"));

        Assert.Equal(1, report.Refused);
        Assert.Equal(0, report.Delivered);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void EachFamilyKeepsItsOwnRouteAndTheReportIsDeterministic(HostKind kind)
    {
        var gate = Gate();
        var (families, _, registrations, _) = Setup(kind, gate);
        registrations.Register(Owner("000A00"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));

        var direct = families.Deliver(Owner("000A00"), Direct("OnDeath"));
        var registered = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged"));

        Assert.Equal(ActorEventPath.DirectOwner, direct.Outcomes[0].Path);
        Assert.Equal(ActorEventPath.RegistrationRequired, registered.Outcomes[0].Path);
        Assert.Equal(direct.ToReport(), families.Deliver(Owner("000A00"), Direct("OnDeath")).ToReport());
        Assert.Equal(64, direct.Digest().Length);
    }

}
