using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ActorEventFamiliesTests
{
    private const string Base = "000800:Npcs.esp";

    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.ReflectionKey, "ENGINE_POLICY"),
        (ActorEventFamilies.DirectKey, "PROVEN"),
        (ActorEventFamilies.RegisteredKey, "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnDeath", "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnCombatStateChanged", "PROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = Rows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static PapyrusOwnerId Owner(string reference) => PapyrusOwnerId.ForPlacedReference(Base, reference);

    private static (ActorEventFamilies Families, PapyrusEventBridge Bridge,
        ScriptRegistrationRegistry Registrations, List<string> Seen) Setup(EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("ActorScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnDeath", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnCombatStateChanged", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var seen = new List<string>();
        host.Overrides = new ScriptOverrideRegistry(gate);
        foreach (var name in new[] { "OnDeath", "OnCombatStateChanged" })
        {
            host.Overrides.Register("ActorScript", name, ScriptOverrideRegistry.AnyParameterCount, context =>
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

    [Fact]
    public void ADirectOwnerEventIsDeliveredWithoutARegistration()
    {
        var gate = Gate();
        var (families, bridge, _, seen) = Setup(gate);

        var report = families.Deliver(Owner("000A00"), Direct("OnDeath"));

        Assert.Equal(1, report.Delivered);
        Assert.Equal(new[] { "OnDeath" }, seen);
        Assert.Equal(1, bridge.Delivered);
    }

    [Fact]
    public void ARegistrationRequiredEventWithoutARegistrationIsRefused()
    {
        var gate = Gate();
        var (families, bridge, _, seen) = Setup(gate);

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged"));

        Assert.Equal(0, report.Delivered);
        Assert.Equal(1, report.Refused);
        Assert.Empty(seen);
        Assert.Equal(0, bridge.Delivered);
        Assert.Contains("did not register", report.Outcomes[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegisteredOwnerReceivesTheEvent()
    {
        var gate = Gate();
        var (families, bridge, registrations, seen) = Setup(gate);
        registrations.Register(Owner("000A00"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged"));

        Assert.Equal(1, report.Delivered);
        Assert.Equal(new[] { "OnCombatStateChanged" }, seen);
        Assert.Equal(1, bridge.Delivered);
    }

    [Fact]
    public void ANonMatchingFilterIsFilteredOut()
    {
        var gate = Gate();
        var (families, _, registrations, seen) = Setup(gate);
        registrations.Register(Owner("000A00"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));
        families.SetFilter(Owner("000A00"), "OnCombatStateChanged", "combat");

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged", "peace"));

        Assert.Equal(0, report.Delivered);
        Assert.Equal(1, report.Filtered);
        Assert.Equal(ActorEventState.FilteredOut, report.Outcomes[0].State);
        Assert.Empty(seen);
    }

    [Fact]
    public void AMatchingFilterIsDelivered()
    {
        var gate = Gate();
        var (families, _, registrations, seen) = Setup(gate);
        registrations.Register(Owner("000A00"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));
        families.SetFilter(Owner("000A00"), "OnCombatStateChanged", "combat");

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged", "combat"));

        Assert.Equal(1, report.Delivered);
        Assert.Equal(new[] { "OnCombatStateChanged" }, seen);
    }

    [Fact]
    public void AnotherOwnersRegistrationDoesNotDeliverHere()
    {
        var gate = Gate();
        var (families, _, registrations, _) = Setup(gate);
        registrations.Register(Owner("000A01"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged"));

        Assert.Equal(1, report.Refused);
        Assert.Equal(0, report.Delivered);
    }

    [Fact]
    public void EachFamilyKeepsItsOwnRouteAndTheReportIsDeterministic()
    {
        var gate = Gate();
        var (families, _, registrations, _) = Setup(gate);
        registrations.Register(Owner("000A00"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));

        var direct = families.Deliver(Owner("000A00"), Direct("OnDeath"));
        var registered = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged"));

        Assert.Equal(ActorEventPath.DirectOwner, direct.Outcomes[0].Path);
        Assert.Equal(ActorEventPath.RegistrationRequired, registered.Outcomes[0].Path);
        Assert.Equal(direct.ToReport(), families.Deliver(Owner("000A00"), Direct("OnDeath")).ToReport());
        Assert.Equal(64, direct.Digest().Length);
    }

    [Fact]
    public void AnUnprovenRouteDeliversNothing()
    {
        var gate = Gate((ActorEventFamilies.RegisteredKey, "UNPROVEN"));
        var (families, _, registrations, seen) = Setup(gate);
        registrations.Register(Owner("000A00"), ScriptRegistration.ForCustomEvent("OnCombatStateChanged"));

        var report = families.Deliver(Owner("000A00"), Registered("OnCombatStateChanged"));

        Assert.Equal(1, report.Refused);
        Assert.Empty(seen);
        Assert.Contains("not proven", report.Outcomes[0].Reason, StringComparison.Ordinal);
    }
}
