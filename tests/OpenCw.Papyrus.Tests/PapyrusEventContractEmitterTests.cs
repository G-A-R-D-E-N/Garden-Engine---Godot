using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class PapyrusEventContractEmitterTests
{
    private static EvidenceRegistry Registry(params (string Key, string Status)[] rows) =>
        EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}"))}]}}");

    [Fact]
    public void EachEmitterNamesItsFamilyAndTheKeyItsRouteNeeds()
    {
        var table = PapyrusEventContract.Emitters.Select(emitter => (emitter.Name, emitter.Family, emitter.FamilyKey)).ToArray();

        Assert.Equal(new[]
        {
            ("OnActivate", "reference activation", "vm.session.activation_routing"),
            ("OnInit", "script lifecycle", "vm.lifecycle.oninit_barrier"),
            ("OnTriggerEnter", "reference trigger", "event.engine_bridge"),
            ("OnTriggerLeave", "reference trigger", "event.engine_bridge"),
            ("OnLoad", "reference lifecycle", "event.reference.lifecycle"),
            ("OnCellAttach", "reference lifecycle", "event.reference.lifecycle"),
            ("OnCellDetach", "reference lifecycle", "event.reference.lifecycle"),
            ("OnUnload", "reference unload", "event.reference.unloaded"),
            ("OnDying", "actor death", "event.actor.direct_owner"),
            ("OnDeath", "actor death", "event.actor.direct_owner"),
            ("Quest.OnStageSet", "quest alias stage event", "quest.alias.runtime"),
        }, table);
    }

    [Fact]
    public void ARegistryThatIsNullIsRejectedByBothClassifiers()
    {
        Assert.Throws<ArgumentNullException>(() => PapyrusEventContract.Classify(null!));
        Assert.Throws<ArgumentNullException>(() => PapyrusEventContract.Classify("OnLoad", null!));
    }

    [Fact]
    public void TheReasonNamesBothKeysAndTheirStatuses()
    {
        var registry = Registry(("event.reference.lifecycle", "ENGINE_POLICY"), ("event.OnLoad", "PROVEN"));

        var support = PapyrusEventContract.Classify("OnLoad", registry);

        Assert.Equal("OnLoad needs event.reference.lifecycle and event.OnLoad; the family key is EnginePolicy and the event key is Proven", support.Reason);
    }

    [Fact]
    public void AnAbsentKeyIsDescribedAsFailingClosed()
    {
        var support = PapyrusEventContract.Classify("OnUnload", Registry(("event.reference.unloaded", "PROVEN")));

        Assert.Equal(EventSupportState.Blocked, support.State);
        Assert.Equal("OnUnload needs event.reference.unloaded and event.OnUnload; the family key is Proven and the event key is absent, which fails closed", support.Reason);
    }

    [Fact]
    public void ABlockedKeyOutranksAnApproximatedOne()
    {
        var registry = Registry(
            ("event.reference.lifecycle", "APPROXIMATED"),
            ("event.OnLoad", "CONTRADICTED"));

        Assert.Equal(EventSupportState.Blocked, PapyrusEventContract.Classify("OnLoad", registry).State);
    }

    [Fact]
    public void AnApproximatedFamilyKeyMakesTheNameApproximatedEvenWithAProvenOwnKey()
    {
        var registry = Registry(
            ("event.reference.lifecycle", "APPROXIMATED"),
            ("event.OnLoad", "PROVEN"));

        var support = PapyrusEventContract.Classify("OnLoad", registry);

        Assert.Equal(EventSupportState.Approximated, support.State);
        Assert.True(support.Deliverable);
    }

    [Fact]
    public void BlockedAndNotEmittedStatesAreNeverDeliverable()
    {
        Assert.True(new EventSupport("n", "k", EventSupportState.Supported, "").Deliverable);
        Assert.True(new EventSupport("n", "k", EventSupportState.Approximated, "").Deliverable);
        Assert.False(new EventSupport("n", "k", EventSupportState.Blocked, "").Deliverable);
        Assert.False(new EventSupport("n", null, EventSupportState.NotEmitted, "").Deliverable);
    }

    [Fact]
    public void ANameGardenDoesNotConstructCarriesItsOwnReason()
    {
        var support = PapyrusEventContract.Classify("OnUnknown", Registry(("event.OnUnknown", "PROVEN")));

        Assert.Equal(new EventSupport("OnUnknown", null, EventSupportState.NotEmitted,
            "Garden constructs no fixed emitter for this name, so no Garden system can emit it"), support);
    }

    [Fact]
    public void TheDeliverableSetHoldsSupportedAndApproximatedNamesOnly()
    {
        var registry = Registry(
            ("event.reference.lifecycle", "ENGINE_POLICY"),
            ("event.OnLoad", "PROVEN"),
            ("event.OnCellAttach", "APPROXIMATED"),
            ("event.OnCellDetach", "UNPROVEN"));

        var names = PapyrusEventContract.Deliverable(registry);

        Assert.Equal(new[] { "OnCellAttach", "OnLoad" }, names.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("onload", names);
    }

    [Theory]
    [InlineData("OnLoad", "OnLoad")]
    [InlineData("Quest.OnStageSet", "::remote_Quest_OnStageSet")]
    [InlineData("A.B.C", "::remote_A_B.C")]
    [InlineData("", "")]
    public void HandlerNameSplitsOnlyAtTheFirstDot(string eventName, string handler)
    {
        Assert.Equal(handler, PapyrusEventContract.HandlerName(eventName));
    }

    [Theory]
    [InlineData("Quest.OnStageSet", "::REMOTE_quest_onstageset", true)]
    [InlineData("OnLoad", "ONLOAD", true)]
    [InlineData("OnLoad", "OnLoad ", false)]
    [InlineData("Quest.OnStageSet", "Quest.OnStageSet", false)]
    public void AHandlerMatchesTheCompiledNameIgnoringCase(string eventName, string handler, bool expected)
    {
        Assert.Equal(expected, PapyrusEventContract.Handles(eventName, handler));
    }

    [Fact]
    public void EveryEmitterIsClassifiedInOrdinalNameOrder()
    {
        var names = PapyrusEventContract.Classify(Registry(("k", "PROVEN"))).Select(support => support.Name).ToList();

        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Equal(PapyrusEventContract.Emitters.Count, names.Count);
        Assert.All(PapyrusEventContract.Classify(Registry(("k", "PROVEN"))),
            support => Assert.Equal(EventSupportState.Blocked, support.State));
    }
}
