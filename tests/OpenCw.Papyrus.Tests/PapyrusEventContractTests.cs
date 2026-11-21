using System;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class PapyrusEventContractTests
{
    private static string Registry(params string[] rows) =>
        $"{{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":[{string.Join(",", rows)}]}}";

    private static string Row(string key, string status) =>
        $"{{\"key\":\"{key}\",\"status\":\"{status}\"}}";

    [Fact]
    public void TheConstructedSetIsExactlyWhatTheRuntimesBuild()
    {
        var registry = EvidenceRegistry.Load(Registry(Row("event.reference.lifecycle", "ENGINE_POLICY")));

        var names = PapyrusEventContract.Classify(registry).Select(support => support.Name).ToArray();

        Assert.Equal(new[] { "OnActivate", "OnCellAttach", "OnCellDetach", "OnDeath", "OnDying", "OnHit", "OnInit", "OnItemAdded", "OnItemRemoved", "OnLoad", "OnTriggerEnter", "OnTriggerLeave", "OnUnload", "Quest.OnStageSet" }, names);
    }

    [Fact]
    public void ARemoteEventIsReceivedByTheCompiledRemoteHandler()
    {
        Assert.Equal("::remote_Quest_OnStageSet", PapyrusEventContract.HandlerName("Quest.OnStageSet"));
        Assert.Equal("OnLoad", PapyrusEventContract.HandlerName("OnLoad"));
        Assert.False(PapyrusEventContract.Handles("Quest.OnStageSet", "OnStageSet"));
    }

    [Fact]
    public void AHandlerMatchesItsEventCaseInsensitivelyAndByItsCompiledName()
    {
        var registry = EvidenceRegistry.Load(Registry(Row("event.reference.lifecycle", "ENGINE_POLICY")));

        Assert.Equal("OnLoad", PapyrusEventContract.Classify("onLoad", registry).Name);
        Assert.Equal("OnUnload", PapyrusEventContract.Classify("OnUnLoad", registry).Name);
        Assert.Equal("quest.alias.runtime", PapyrusEventContract.Classify("::remote_Quest_OnStageSet", registry).FamilyKey);
        Assert.Equal(EventSupportState.NotEmitted, PapyrusEventContract.Classify("::remote_Actor_OnDeath", registry).State);
    }

    [Fact]
    public void EveryListedNameRoundTripsThroughTheSingleNameLookup()
    {
        var registry = EvidenceRegistry.Load(Registry(Row("event.reference.lifecycle", "ENGINE_POLICY")));

        foreach (var listed in PapyrusEventContract.Classify(registry))
        {
            Assert.Equal(listed, PapyrusEventContract.Classify(listed.Name, registry));
            Assert.Equal(listed, PapyrusEventContract.Classify(PapyrusEventContract.HandlerName(listed.Name), registry));
        }
        Assert.Equal("Quest.OnStageSet", PapyrusEventContract.Classify("quest.onstageset", registry).Name);
        Assert.Equal(EventSupportState.NotEmitted, PapyrusEventContract.Classify("OnStageSet", registry).State);
    }

    [Fact]
    public void OnInitWithoutItsOwnRowIsUnprovenButStillDeliverable()
    {
        var registry = EvidenceRegistry.Load(Registry(Row("vm.lifecycle.oninit_barrier", "ENGINE_POLICY")));

        var support = PapyrusEventContract.Classify("OnInit", registry);

        Assert.Equal(EventSupportState.Unproven, support.State);
        Assert.True(support.Deliverable);
        Assert.Equal("vm.lifecycle.oninit_barrier", support.FamilyKey);
    }

    [Fact]
    public void ADeliverableEventNeedsItsFamilyKeyAndItsOwnKey()
    {
        var registry = EvidenceRegistry.Load(Registry(
            Row("event.reference.lifecycle", "ENGINE_POLICY"),
            Row("event.OnLoad", "PROVEN")));

        var support = PapyrusEventContract.Classify("OnLoad", registry);

        Assert.Equal(EventSupportState.Supported, support.State);
        Assert.True(support.Deliverable);
        Assert.Equal("event.reference.lifecycle", support.FamilyKey);
    }

    [Fact]
    public void AnAllowedFamilyWithoutItsOwnKeyIsUnprovenButStillDelivers()
    {
        var registry = EvidenceRegistry.Load(Registry(Row("event.reference.lifecycle", "ENGINE_POLICY")));

        var support = PapyrusEventContract.Classify("OnLoad", registry);

        Assert.Equal(EventSupportState.Unproven, support.State);
        Assert.True(support.Deliverable);
        Assert.Contains("absent", support.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnprovenFamilyKeyMarksEveryNameOfThatFamilyUnproven()
    {
        var registry = EvidenceRegistry.Load(Registry(
            Row("event.reference.unloaded", "UNPROVEN"),
            Row("event.OnUnload", "PROVEN")));

        var support = PapyrusEventContract.Classify("OnUnload", registry);

        Assert.Equal(EventSupportState.Unproven, support.State);
        Assert.True(support.Deliverable);
    }

    [Fact]
    public void AnApproximatedKeyIsDeliverableAndCounted()
    {
        var registry = EvidenceRegistry.Load(Registry(
            Row("event.reference.lifecycle", "ENGINE_POLICY"),
            Row("event.OnLoad", "APPROXIMATED")));

        var support = PapyrusEventContract.Classify("OnLoad", registry);

        Assert.Equal(EventSupportState.Approximated, support.State);
        Assert.True(support.Deliverable);
    }

    [Fact]
    public void ANameGardenDoesNotConstructIsNeverDeliverable()
    {
        var registry = EvidenceRegistry.Load(Registry(Row("event.OnUnknown", "PROVEN")));

        var support = PapyrusEventContract.Classify("OnUnknown", registry);

        Assert.Equal(EventSupportState.NotEmitted, support.State);
        Assert.False(support.Deliverable);
        Assert.Null(support.FamilyKey);
    }

    [Fact]
    public void TheRecordedRegistryDeliversEveryNameAndMarksTheOnesWithoutARecordedRowUnproven()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(
            File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));

        var recorded = new[] { "OnDeath", "OnDying", "OnHit", "OnItemAdded", "OnItemRemoved" };
        Assert.Equal(PapyrusEventContract.Emitters.Select(emitter => emitter.Name).Order(StringComparer.Ordinal),
            PapyrusEventContract.Deliverable(registry).Order(StringComparer.Ordinal));
        Assert.Equal(recorded, PapyrusEventContract.Classify(registry)
            .Where(support => support.State != EventSupportState.Unproven).Select(support => support.Name)
            .Order(StringComparer.Ordinal));
        Assert.All(PapyrusEventContract.Classify(registry).Where(support => !recorded.Contains(support.Name)),
            support => Assert.Equal(EventSupportState.Unproven, support.State));
        Assert.All(new[] { "OnDeath", "OnDying" },
            name => Assert.Equal(EventSupportState.Approximated, PapyrusEventContract.Classify(name, registry).State));
    }

    [Fact]
    public void TheRecordedRegistryBindsTheCombatSystem()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var gate = new EvidenceGate(EvidenceRegistry.Load(
            File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json"))));

        Assert.True(gate.Gate("system.combat").Allowed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActivationIsAlwaysDeliverableAndUnprovenWithoutItsOwnEvidence(bool ownAllowed)
    {
        var registry = EvidenceRegistry.Load(Registry(
            Row("vm.session.activation_routing", "ENGINE_POLICY"),
            Row("event.OnActivate", ownAllowed ? "ENGINE_POLICY" : "UNPROVEN")));
        var support = PapyrusEventContract.Classify("OnActivate", registry);
        Assert.Equal("vm.session.activation_routing", support.FamilyKey);
        Assert.True(support.Deliverable);
        Assert.Equal(ownAllowed ? EventSupportState.Supported : EventSupportState.Unproven, support.State);
    }
}
