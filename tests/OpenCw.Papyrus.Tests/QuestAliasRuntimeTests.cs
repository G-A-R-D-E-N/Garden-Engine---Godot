using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vmad;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class QuestAliasRuntimeTests
{
    [Fact]
    public void ReviewAttachedCountExcludesFilledAliasesWithoutScripts()
    {
        var (runtime, host, _, _, _) = Setup(Gate());
        var report = runtime.Attach(Fills(1, 2), new[] { Script(1) });
        Assert.Equal(1, report.Attached);
        Assert.Equal(1, report.Skipped);
        Assert.Equal(2, report.Aliases.Count);
        Assert.Equal(report.Attached, runtime.Attached);
        Assert.Single(host.Instances);
    }

    private const string Quest = "000800:Quests.esp";

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
        (EvidenceKeys.NativeTarget, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.ReflectionKey, "ENGINE_POLICY"),
        (QuestAliasRuntime.RuntimeKey, "ENGINE_POLICY"),
        ("vmad.value_binding", "ENGINE_POLICY"),
        ("owner.engine_identity", "ENGINE_POLICY"),
        (PapyrusEventBridge.KeyPrefix + QuestAliasRuntime.StageEventName, "PROVEN"),
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

    private static PapyrusOwnerId Owner(int aliasId) => PapyrusOwnerId.ForQuestAlias(Quest, aliasId);

    private static AliasFillReport Fills(params int[] filledAliases) =>
        new(filledAliases.Select(id => new AliasOutcome(Owner(id), AliasFillKind.ForcedReference,
                AliasState.Filled, "000A00", "filled")).ToList(),
            filledAliases.Length, 0, Array.Empty<string>());

    private static readonly string Handler = PapyrusEventContract.HandlerName(QuestAliasRuntime.StageEventName);

    private static AliasFillReport OpenOnly() =>
        new(new[] { new AliasOutcome(Owner(9), AliasFillKind.FindMatching, AliasState.Open, null, "open") },
            0, 1, new[] { "FindMatching" });

    private static (QuestAliasRuntime Runtime, ScriptHost Host, PapyrusEventBridge Bridge,
        ScriptRegistrationRegistry Registrations, List<string> Seen) Setup(EvidenceGate gate, PapyrusInstruction? onInit = null)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("AliasScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { onInit ?? PapyrusInstruction.Return() }),
            new PapyrusFunction(Handler, new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var seen = new List<string>();
        host.Overrides = new ScriptOverrideRegistry(gate);
        host.Overrides.Register("AliasScript", Handler,
            ScriptOverrideRegistry.AnyParameterCount, context =>
            {
                seen.Add(context.Function);
                return OverrideResult.Handled(PapyrusValue.None);
            });
        var bridge = new PapyrusEventBridge(host, gate);
        var registrations = new ScriptRegistrationRegistry();
        return (new QuestAliasRuntime(host, bridge, registrations, gate), host, bridge, registrations, seen);
    }

    private static QuestAliasScript Script(int aliasId, params int[] objectives) =>
        new(Quest, aliasId, "AliasScript", Array.Empty<VmadValueShape>(), objectives);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FailedInitializationIsRefusedWithoutAliasPublication(int failure)
    {
        var (runtime, host, bridge, registrations, seen) = Setup(Gate(), PapyrusInstruction.Unsupported("synthetic alias init failure"));
        var initialized = new List<long>();
        host.Overrides!.Register("AliasScript", "OnInit", 0, context =>
        {
            var count = context.Instance.Properties["Count"].Integer;
            initialized.Add(count);
            if (count != 2) return OverrideResult.Handled(PapyrusValue.None);
            if (failure == 1) throw new InvalidOperationException("synthetic alias init failure");
            return failure == 0 ? OverrideResult.Refused("synthetic alias init failure") : OverrideResult.Declined();
        });
        var scripts = Enumerable.Range(1, 3).Select(id => Script(id, id * 10) with
        {
            Properties = new[] { new VmadValueShape("Count", "Int", Integer: id) },
        });
        var report = runtime.Attach(Fills(1, 2, 3), scripts);

        Assert.Equal(2, report.Attached);
        Assert.Equal(1, report.Refused);
        Assert.Equal(0, report.Skipped);
        Assert.Equal(2, report.Properties);
        Assert.Equal(2, report.Natives);
        Assert.Equal(2, report.Objectives);
        Assert.Equal(report.Attached, runtime.Attached);
        Assert.Equal(report.Refused, runtime.Refused);
        Assert.Equal(new long[] { 1, 2, 3 }, initialized);
        Assert.Equal(new[] { ScriptLifecycle.Ready, ScriptLifecycle.Failed, ScriptLifecycle.Ready }, host.Instances.Select(instance => instance.Lifecycle));
        Assert.Equal(AliasAttachState.Refused, report.Aliases[1].State);
        Assert.Contains(failure == 2 ? "vm.ir.unsupported_operation" : "synthetic alias init failure", report.Aliases[1].Reason, StringComparison.Ordinal);
        Assert.Equal(0, report.Aliases[1].Properties);
        Assert.Equal(0, report.Aliases[1].Natives);
        Assert.Equal(0, report.Aliases[1].Objectives);
        Assert.Equal(-1, runtime.ObjectiveOf(Owner(2), 20));
        Assert.True(host.TryRegisterNative(new NativeId("Alias", $"SetObjective{Owner(2).Discriminator}"),
            _ => PapyrusValue.FromBool(true), NativeTargetKind.Static, "Alias"));
        foreach (var owner in new[] { Owner(1), Owner(2), Owner(3) })
            registrations.Register(owner, ScriptRegistration.ForRemoteEvent(PapyrusOwnerId.ForForm(Quest), QuestAliasRuntime.StageEventName));
        var stages = runtime.StageSet(new[] { Owner(1), Owner(2), Owner(3) }, PapyrusOwnerId.ForForm(Quest));
        Assert.Equal(2, stages.StageDelivered);
        Assert.Equal(1, stages.StageRefused);
        Assert.Equal("UnknownOwner", stages.Stages[1].Reason);
        Assert.Equal(2, bridge.Delivered);
        Assert.Equal(new[] { Handler, Handler }, seen);
        Assert.Equal(0, host.RefusedEvents);
        Assert.False(host.Initialize(host.Instances[1]).Completed);
        Assert.Equal(3, initialized.Count);
    }

    [Fact]
    public void BlockedInitializationNeverReportsAliasAttachment()
    {
        var (runtime, host, _, _, _) = Setup(Gate((EvidenceKeys.OnInitBarrier, "UNPROVEN")));
        var report = runtime.Attach(Fills(1), new[] { Script(1, 10) });
        Assert.Equal(0, report.Attached);
        Assert.Equal(1, report.Refused);
        Assert.Equal(AliasAttachState.Refused, Assert.Single(report.Aliases).State);
        Assert.Contains(EvidenceKeys.OnInitBarrier, report.Aliases[0].Reason, StringComparison.Ordinal);
        Assert.Equal(ScriptLifecycle.NotInitialized, Assert.Single(host.Instances).Lifecycle);
        Assert.Empty(runtime.ObjectiveState);
        Assert.Equal(0, report.Properties);
        Assert.Equal(0, report.Natives);
        Assert.Equal(0, report.Objectives);
    }

    [Fact]
    public void AFilledAliasRunsItsOwnScript()
    {
        var gate = Gate();
        var (runtime, host, _, _, _) = Setup(gate);

        var report = runtime.Attach(Fills(1), new[] { Script(1) });

        Assert.Equal(1, report.Attached);
        Assert.Equal(AliasAttachState.Attached, report.Aliases[0].State);
        Assert.Equal(1, runtime.Attached);
        Assert.Equal(1, host.Instances.Count);
    }

    [Fact]
    public void AnOpenAliasIsSkippedAndCounted()
    {
        var gate = Gate();
        var (runtime, host, _, _, _) = Setup(gate);

        var report = runtime.Attach(OpenOnly(), new[] { Script(9) });

        Assert.Equal(0, report.Attached);
        Assert.Empty(report.Aliases);
        Assert.Equal(0, host.Instances.Count);
    }

    [Fact]
    public void PropertiesBindToTheAliasOwner()
    {
        var gate = Gate();
        var (runtime, host, _, _, _) = Setup(gate);
        var observed = new List<IReadOnlyDictionary<string, PapyrusValue>>();
        host.Overrides!.Register("AliasScript", "OnInit", 0, context =>
        {
            observed.Add(context.Instance.Properties);
            return OverrideResult.Handled(PapyrusValue.None);
        });
        var values = new[]
        {
            new VmadValueShape("Count", "Int", Integer: 7),
            new VmadValueShape("SignedZero", "Float", Real: -0.0d),
            new VmadValueShape("Flag", "Bool", Boolean: false),
            new VmadValueShape("Text", "String", Text: "a text"),
            new VmadValueShape("Empty", "String", Text: ""),
            new VmadValueShape("Zero", "Int", Integer: 0),
            new VmadValueShape("Link", "Object", Object: "000123:Quests.esp"),
        };
        var script = Script(1) with { Properties = values };
        var other = Script(2) with { Properties = new[] { values[0] with { Integer = 11 } } };

        var report = runtime.Attach(Fills(1, 2), new[] { other, script });

        Assert.Equal(2, observed.Count);
        Assert.Equal(7, observed[0]["count"].Integer);
        Assert.Equal(11, observed[1]["Count"].Integer);
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0d), BitConverter.DoubleToInt64Bits(observed[0]["SignedZero"].Real));
        Assert.False(observed[0]["Flag"].Flag);
        Assert.Equal("a text", observed[0]["Text"].Text);
        Assert.Equal("", observed[0]["Empty"].Text);
        Assert.Equal(0, observed[0]["Zero"].Integer);
        Assert.Equal("000123:Quests.esp", Assert.IsType<EngineObjectReference>(observed[0]["Link"].Reference).Identity);
        Assert.Equal(8, report.Properties);
        Assert.Equal(8, runtime.Properties);
        Assert.Equal(2, report.Attached);
        Assert.Equal(new[] { Owner(1), Owner(2) }, report.Aliases.Select(alias => alias.Owner));
        Assert.Equal(new[] { 7, 1 }, report.Aliases.Select(alias => alias.Properties));
        for (var index = 0; index < observed.Count; index++)
            Assert.Same(observed[index], host.Instances[index].Properties);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void UnreproduciblePropertiesRefuseBeforeAliasMutation(int defect)
    {
        var (runtime, host, bridge, registrations, seen) = Setup(Gate());
        var initialized = 0;
        host.Overrides!.Register("AliasScript", "OnInit", 0, _ =>
        {
            initialized++;
            return OverrideResult.Handled(PapyrusValue.None);
        });
        var invalid = defect switch
        {
            0 => new VmadValueShape("Bad", "Array", ElementCount: 2),
            1 => new VmadValueShape("Bad", "Struct", ElementCount: 2),
            2 => new VmadValueShape("Bad", "Variable"),
            3 => new VmadValueShape("Bad", "Object", HasAlias: true, Object: "000123:Quests.esp"),
            4 => new VmadValueShape("Bad", "Int"),
            5 => new VmadValueShape("Bad", "Float"),
            6 => new VmadValueShape("Bad", "Bool"),
            7 => new VmadValueShape("Bad", "String"),
            8 => new VmadValueShape("Bad", "Object"),
            9 => new VmadValueShape("", "Int", Integer: 7),
            10 => new VmadValueShape(" ", "Int", Integer: 7),
            _ => new VmadValueShape("COUNT", "Int", Integer: 11),
        };
        var script = Script(1, 10) with
        {
            Properties = new[] { new VmadValueShape("Count", "Int", Integer: 7), invalid },
        };
        var report = runtime.Attach(Fills(1, 2, 3), new[] { script, Script(2, 20) });

        Assert.Equal(1, report.Attached);
        Assert.Equal(1, report.Skipped);
        Assert.Equal("Refused", report.Aliases[0].State.ToString());
        Assert.Equal(1, report.Refused);
        Assert.Equal(1, runtime.Refused);
        Assert.Equal(report.Aliases.Count, report.Attached + report.Skipped + report.Refused);
        Assert.NotEmpty(report.Aliases[0].Reason);
        Assert.Equal(0, report.Aliases[0].Properties);
        Assert.Equal(0, report.Aliases[0].Natives);
        Assert.Equal(0, report.Aliases[0].Objectives);
        Assert.Equal(0, report.Properties);
        Assert.Equal(1, report.Natives);
        Assert.Equal(1, report.Objectives);
        Assert.Equal(1, initialized);
        Assert.Single(host.Instances);
        Assert.True(host.TryRegisterNative(new NativeId("Alias", $"SetObjective{Owner(1).Discriminator}"),
            _ => PapyrusValue.FromBool(true), NativeTargetKind.Static, "Alias"));
        Assert.Equal(-1, runtime.ObjectiveOf(Owner(1), 10));
        Assert.Equal(1, runtime.ObjectiveOf(Owner(2), 20));
        Assert.Contains("refused=1", report.ToReport(), StringComparison.Ordinal);
        registrations.Register(Owner(1), ScriptRegistration.ForRemoteEvent(PapyrusOwnerId.ForForm(Quest), QuestAliasRuntime.StageEventName));
        var stages = runtime.StageSet(new[] { Owner(1) }, PapyrusOwnerId.ForForm(Quest));
        Assert.Equal(0, stages.StageDelivered);
        Assert.Equal(1, stages.StageRefused);
        Assert.Equal(1, stages.Refused);
        Assert.Equal(0, bridge.Delivered);
        Assert.Empty(seen);
    }

    [Theory]
    [InlineData(QuestAliasRuntime.RuntimeKey, false)]
    [InlineData(QuestAliasRuntime.RuntimeKey, true)]
    [InlineData("vmad.value_binding", true)]
    [InlineData("owner.engine_identity", true)]
    public void AliasBindingHonorsEvidenceBeforeMutation(string key, bool properties)
    {
        var (runtime, host, _, _, _) = Setup(Gate((key, "UNPROVEN")));
        var script = Script(1, 10);
        if (properties)
            script = script with { Properties = new[] { new VmadValueShape("Link", "Object", Object: "000123:Quests.esp") } };
        var report = runtime.Attach(Fills(1), new[] { script });
        Assert.Equal(0, report.Attached);
        Assert.Equal(1, report.Refused);
        Assert.Equal(1, runtime.Refused);
        Assert.Equal("Refused", Assert.Single(report.Aliases).State.ToString());
        Assert.Contains(key, report.Aliases[0].Reason, StringComparison.Ordinal);
        Assert.Empty(host.Instances);
        Assert.Empty(runtime.ObjectiveState);
        Assert.Equal(0, report.Properties);
        Assert.Equal(0, report.Natives);
        Assert.Equal(0, report.Objectives);
    }

    [Fact]
    public void PropertyGatesApplyOnlyToValuesThatNeedThem()
    {
        var (runtime, host, _, _, _) = Setup(Gate(("vmad.value_binding", "UNPROVEN"), ("owner.engine_identity", "UNPROVEN")));
        Assert.Equal(1, runtime.Attach(Fills(1), new[] { Script(1) }).Attached);
        Assert.Single(host.Instances);
        (runtime, host, _, _, _) = Setup(Gate(("owner.engine_identity", "UNPROVEN")));
        var scalar = Script(1) with { Properties = new[] { new VmadValueShape("Count", "Int", Integer: 7) } };
        Assert.Equal(1, runtime.Attach(Fills(1), new[] { scalar }).Attached);
        Assert.Equal(7, Assert.Single(host.Instances).Properties["Count"].Integer);
    }

    [Fact]
    public void ObjectiveStateIsAuthoritativeHere()
    {
        var gate = Gate();
        var (runtime, _, _, _, _) = Setup(gate);

        var report = runtime.Attach(Fills(1), new[] { Script(1, 10, 20) });

        Assert.Equal(2, report.Objectives);
        Assert.Equal(1, runtime.ObjectiveOf(Owner(1), 10));
        Assert.Equal(1, runtime.ObjectiveOf(Owner(1), 20));
        Assert.Equal(-1, runtime.ObjectiveOf(Owner(1), 30));

        runtime.SetObjective(Owner(1), 10, 2);
        Assert.Equal(2, runtime.ObjectiveOf(Owner(1), 10));
    }

    [Fact]
    public void ARegisteredAliasReceivesTheStageEvent()
    {
        var gate = Gate();
        var (runtime, _, bridge, registrations, seen) = Setup(gate);
        runtime.Attach(Fills(1), new[] { Script(1) });
        registrations.Register(Owner(1), ScriptRegistration.ForRemoteEvent(PapyrusOwnerId.ForForm(Quest), QuestAliasRuntime.StageEventName));

        var report = runtime.StageSet(new[] { Owner(1) }, PapyrusOwnerId.ForForm(Quest));

        Assert.Equal(1, report.StageDelivered);
        Assert.True(report.Stages[0].Delivered);
        Assert.Equal(new[] { Handler }, seen);
        Assert.Equal(1, bridge.Delivered);
    }

    [Fact]
    public void AnUnregisteredAliasReceivesNothing()
    {
        var gate = Gate();
        var (runtime, _, bridge, _, seen) = Setup(gate);
        runtime.Attach(Fills(1), new[] { Script(1) });

        var report = runtime.StageSet(new[] { Owner(1) }, PapyrusOwnerId.ForForm(Quest));

        Assert.Equal(0, report.StageDelivered);
        Assert.Equal(1, report.StageRefused);
        Assert.False(report.Stages[0].Delivered);
        Assert.Contains("did not register", report.Stages[0].Reason, StringComparison.Ordinal);
        Assert.Empty(seen);
        Assert.Equal(0, bridge.Delivered);
        Assert.Equal(1, runtime.StageRefused);
    }

    [Fact]
    public void TheReportsAreDeterministicAndCounted()
    {
        var gate = Gate();
        var (runtime, _, _, registrations, _) = Setup(gate);
        var scripts = new[] { Script(2, 5), Script(1) };

        var first = runtime.Attach(Fills(1, 2), scripts);
        var second = runtime.Attach(Fills(1, 2), scripts);
        registrations.Register(Owner(1), ScriptRegistration.ForRemoteEvent(PapyrusOwnerId.ForForm(Quest), QuestAliasRuntime.StageEventName));

        var stages = runtime.StageSet(new[] { Owner(1), Owner(2) }, PapyrusOwnerId.ForForm(Quest));
        var reversed = runtime.StageSet(new[] { Owner(2), Owner(1) }, PapyrusOwnerId.ForForm(Quest));

        Assert.Equal(first.Aliases.Count, first.Attached);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
        Assert.Equal(stages.Stages.Count, stages.StageDelivered + stages.StageRefused);
        Assert.Equal(stages.Stages.Select(stage => stage.Owner.Key), reversed.Stages.Select(stage => stage.Owner.Key));
    }
}
