using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class QuestAliasActorBindingTests
{
    private const string Quest = "000800:Quests.esp";
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
        (PapyrusEventBridge.KeyPrefix + "Test", "PROVEN"),
        (QuestAliasActorBinding.BindingKey, "PROVEN"),
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

    private static PapyrusOwnerId Alias(int aliasId) => PapyrusOwnerId.ForQuestAlias(Quest, aliasId);

    private static PapyrusOwnerId Actor(string reference) => PapyrusOwnerId.ForPlacedReference(Base, reference);

    private static (QuestAliasActorBinding Binding, ScriptHost Host, PapyrusEventBridge Bridge) Setup(EvidenceGate gate, PapyrusInstruction? onInit = null)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("AliasScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { onInit ?? PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var bridge = new PapyrusEventBridge(host, gate);
        return (new QuestAliasActorBinding(host, bridge, gate), host, bridge);
    }

    private static ActorAliasReport Filled(params (int Alias, string Reference)[] filled) =>
        new(filled.Select(pair => new ActorAliasOutcome(Alias(pair.Alias), AliasFillKind.UniqueActor,
                ActorAliasState.Filled, Actor(pair.Reference), "filled")).ToList(),
            filled.Length, 0, 0, Array.Empty<string>());

    private static ActorAliasReport Unresolved(int aliasId, AliasFillKind kind) =>
        new(new[] { new ActorAliasOutcome(Alias(aliasId), kind, ActorAliasState.Unfilled, null,
                "the condition list is indeterminate, so the alias stays unbound") },
            0, 1, 0, Array.Empty<string>());

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FailedInitializationKeepsTheTargetWithoutPublishingItsScript(int failure)
    {
        var gate = Gate();
        var (binding, host, bridge) = Setup(gate, PapyrusInstruction.Unsupported("synthetic actor alias init failure"));
        host.Overrides = new ScriptOverrideRegistry(gate);
        var initialized = 0;
        host.Overrides.Register("AliasScript", "OnInit", 0, _ =>
        {
            initialized++;
            if (initialized != 2) return OverrideResult.Handled(PapyrusValue.None);
            if (failure == 1) throw new InvalidOperationException("synthetic actor alias init failure");
            return failure == 0 ? OverrideResult.Refused("synthetic actor alias init failure") : OverrideResult.Declined();
        });
        var scripts = Enumerable.Range(1, 3).Select(id => new QuestAliasScript(Quest, id, "AliasScript",
            Array.Empty<OpenCommonwealth.Services.Papyrus.Vmad.VmadValueShape>(), Array.Empty<int>()));

        var report = binding.Bind(Filled((1, "000A00"), (2, "000A01"), (3, "000A02")), scripts);

        Assert.Equal(2, report.Attached);
        Assert.Equal(2, binding.Attached);
        Assert.Equal(3, report.Bound);
        Assert.Equal(3, binding.Bound);
        Assert.Equal(0, report.Unbound);
        Assert.Equal(0, binding.Unbound);
        Assert.Empty(report.Unresolved);
        Assert.All(report.Bindings, item => Assert.True(item.Bound));
        Assert.Equal(Actor("000A01"), binding.ActorOf(Alias(2)));
        Assert.Equal("AliasScript", report.Bindings[1].Script);
        Assert.Contains(failure == 2 ? "vm.ir.unsupported_operation" : "synthetic actor alias init failure", report.Bindings[1].Reason, StringComparison.Ordinal);
        Assert.NotNull(binding.InstanceOf(Alias(1)));
        Assert.Null(binding.InstanceOf(Alias(2)));
        Assert.NotNull(binding.InstanceOf(Alias(3)));
        Assert.Equal(2, bridge.RegisteredOwners);
        Assert.Equal(EventDelivery.UnknownOwner, Assert.Single(bridge.Send(new EngineEvent("Test", Array.Empty<PapyrusValue>(), Alias(2))).Outcomes).Result);
        Assert.Equal(0, bridge.Delivered);
        Assert.Equal(0, host.RefusedEvents);
        Assert.Equal(new[] { ScriptLifecycle.Ready, ScriptLifecycle.Failed, ScriptLifecycle.Ready }, host.Instances.Select(instance => instance.Lifecycle));
        Assert.False(host.Initialize(host.Instances[1]).Completed);
        Assert.Equal(3, initialized);
    }

    [Fact]
    public void BlockedInitializationKeepsTheTargetWithoutPublishingItsScript()
    {
        var (binding, host, bridge) = Setup(Gate((EvidenceKeys.OnInitBarrier, "UNPROVEN")));
        var report = binding.Bind(Filled((1, "000A00")), new[]
        {
            new QuestAliasScript(Quest, 1, "AliasScript", Array.Empty<OpenCommonwealth.Services.Papyrus.Vmad.VmadValueShape>(), Array.Empty<int>()),
        });

        Assert.Equal(0, report.Attached);
        Assert.Equal(0, binding.Attached);
        Assert.Equal(1, report.Bound);
        Assert.Equal(0, report.Unbound);
        Assert.True(Assert.Single(report.Bindings).Bound);
        Assert.Contains(EvidenceKeys.OnInitBarrier, report.Bindings[0].Reason, StringComparison.Ordinal);
        Assert.Equal(Actor("000A00"), binding.ActorOf(Alias(1)));
        Assert.Null(binding.InstanceOf(Alias(1)));
        Assert.Equal(0, bridge.RegisteredOwners);
        Assert.Equal(ScriptLifecycle.NotInitialized, Assert.Single(host.Instances).Lifecycle);
    }

    [Fact]
    public void AFilledAliasBindsToItsActorAndRunsItsScript()
    {
        var gate = Gate();
        var (binding, host, _) = Setup(gate);

        var report = binding.Bind(Filled((1, "000A00")), new[]
        {
            new QuestAliasScript(Quest, 1, "AliasScript", Array.Empty<OpenCommonwealth.Services.Papyrus.Vmad.VmadValueShape>(), Array.Empty<int>()),
        });

        Assert.Equal(1, report.Bound);
        Assert.Equal(1, report.Attached);
        Assert.Equal(Actor("000A00"), binding.ActorOf(Alias(1)));
        Assert.NotNull(binding.InstanceOf(Alias(1)));
        Assert.Equal(1, host.Instances.Count);
    }

    [Fact]
    public void AFindMatchingAliasBindsToTheMatchingActor()
    {
        var gate = Gate();
        var (binding, _, _) = Setup(gate);
        var report = new ActorAliasReport(new[]
        {
            new ActorAliasOutcome(Alias(2), AliasFillKind.FindMatching, ActorAliasState.Filled, Actor("000A01"), "matched"),
        }, 1, 0, 0, Array.Empty<string>());

        binding.Bind(report, Array.Empty<QuestAliasScript>());

        Assert.Equal(Actor("000A01"), binding.ActorOf(Alias(2)));
    }

    [Fact]
    public void TwoAliasesKeepTheirOwnActors()
    {
        var gate = Gate();
        var (binding, _, _) = Setup(gate);

        binding.Bind(Filled((1, "000A00"), (2, "000A01")), Array.Empty<QuestAliasScript>());

        Assert.Equal(Actor("000A00"), binding.ActorOf(Alias(1)));
        Assert.Equal(Actor("000A01"), binding.ActorOf(Alias(2)));
        Assert.NotEqual(binding.ActorOf(Alias(1)), binding.ActorOf(Alias(2)));
    }

    [Fact]
    public void AnUnresolvedAliasStaysUnboundAndIsNamed()
    {
        var gate = Gate();
        var (binding, host, _) = Setup(gate);

        var report = binding.Bind(Unresolved(3, AliasFillKind.FindMatching), Array.Empty<QuestAliasScript>());

        Assert.Equal(0, report.Bound);
        Assert.Equal(1, report.Unbound);
        Assert.Null(binding.ActorOf(Alias(3)));
        Assert.Null(binding.InstanceOf(Alias(3)));
        Assert.Equal(0, host.Instances.Count);
        Assert.Single(report.Unresolved);
        Assert.Equal(Alias(3).Key, report.Unresolved[0].Name);
        Assert.Contains("indeterminate", report.Unresolved[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenAliasIsNamedToo()
    {
        var gate = Gate();
        var (binding, _, _) = Setup(gate);
        var report = new ActorAliasReport(new[]
        {
            new ActorAliasOutcome(Alias(4), AliasFillKind.UniqueActor, ActorAliasState.Open, null,
                "the actor fill is not proven"),
        }, 0, 0, 1, new[] { "UniqueActor" });

        var result = binding.Bind(report, Array.Empty<QuestAliasScript>());

        Assert.Single(result.Unresolved);
        Assert.Equal(Alias(4).Key, result.Unresolved[0].Name);
        Assert.Null(binding.ActorOf(Alias(4)));
    }

    [Fact]
    public void AnUnprovenBindingKeyLeavesTheAliasUnboundAndNamed()
    {
        var gate = Gate((QuestAliasActorBinding.BindingKey, "UNPROVEN"));
        var (binding, _, _) = Setup(gate);

        var report = binding.Bind(Filled((1, "000A00")), Array.Empty<QuestAliasScript>());

        Assert.Equal(0, report.Bound);
        Assert.Equal(1, report.Unbound);
        Assert.Null(binding.ActorOf(Alias(1)));
        Assert.Contains("not proven", report.Unresolved[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportIsDeterministicAndCountsEveryAlias()
    {
        var gate = Gate();
        var (binding, _, _) = Setup(gate);
        var filled = Filled((2, "000A01"), (1, "000A00"));

        var first = binding.Bind(filled, Array.Empty<QuestAliasScript>());
        var second = binding.Bind(filled, Array.Empty<QuestAliasScript>());

        Assert.Equal(first.Bindings.Count, first.Bound + first.Unbound);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
        Assert.Equal(2, first.Bound);
    }
}
