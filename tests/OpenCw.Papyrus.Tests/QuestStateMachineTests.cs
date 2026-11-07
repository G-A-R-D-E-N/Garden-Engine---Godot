using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class QuestStateMachineTests
{
    [Fact]
    public void ReviewQuestBindingRefusesAmbiguousStaticOwnership()
    {
        var gate = Gate();
        var host = Host(gate);
        var machine = new QuestStateMachine(gate);
        var report = machine.Bind(host, new[] { Quest, "000801:Quests.esp" });
        Assert.Equal(4, report.Bound);
        Assert.Equal(2, report.Blocked);
        Assert.Throws<InvalidOperationException>(() => ((INativeTarget)host).Invoke(
            new NativeId("Quest", "Start"), NativeTargetKind.Static, PapyrusValue.None,
            Array.Empty<PapyrusValue>()));
        Assert.Equal(0, machine.Applied);
    }

    [Fact]
    public void ReviewQuestRebindingCannotOverwriteAnotherQuest()
    {
        var gate = Gate();
        var host = Host(gate);
        var first = new QuestStateMachine(gate);
        var second = new QuestStateMachine(gate);
        first.Bind(host, new[] { Quest });
        var report = second.Bind(host, new[] { "000801:Quests.esp" });
        ((INativeTarget)host).Invoke(new NativeId("Quest", "Start"), NativeTargetKind.EngineObject,
            PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Quest))),
            Array.Empty<PapyrusValue>());
        Assert.Equal(QuestState.Running, first.StateOf(Quest));
        Assert.Equal(QuestState.NotStarted, second.StateOf("000801:Quests.esp"));
        Assert.Equal(0, report.Bound);
    }

    private const string Quest = "000800:Quests.esp";

    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.NativeTarget, "ENGINE_POLICY"),
        ("owner.engine_identity", "ENGINE_POLICY"),
        (QuestStateMachine.LifecycleKey, "PROVEN"),
        (QuestStateMachine.SetActiveKey, "UNPROVEN"),
        (QuestStageDispatch.TimingKey, "UNPROVEN"),
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

    private static ScriptHost Host(EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("QuestScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        return new ScriptHost(library, gate);
    }

    [Fact]
    public void TheLifecycleTransitionsAreProven()
    {
        var machine = new QuestStateMachine(Gate());
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));

        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.Equal(QuestState.Running, machine.StateOf(Quest));

        Assert.True(machine.Apply(Quest, QuestNative.Stop));
        Assert.Equal(QuestState.Stopped, machine.StateOf(Quest));

        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.CompleteQuest));
        Assert.Equal(QuestState.Completed, machine.StateOf(Quest));

        Assert.True(machine.Apply(Quest, QuestNative.Reset));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
        Assert.Equal(5, machine.Applied);
    }

    [Fact]
    public void AnIllegalTransitionIsRefusedAndCounted()
    {
        var machine = new QuestStateMachine(Gate());

        Assert.False(machine.Apply(Quest, QuestNative.CompleteQuest));
        Assert.False(machine.Apply(Quest, QuestNative.Stop));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
        Assert.Equal(2, machine.Refused);
        Assert.Equal(0, machine.Applied);
    }

    [Fact]
    public void SetActiveIsBlockedByItsNamedKeyAndNeverTransitions()
    {
        var gate = Gate();
        var machine = new QuestStateMachine(gate);
        var report = machine.Bind(Host(gate), new[] { Quest });

        var binding = report.Natives.Single(native => native.Native == QuestNative.SetActive);
        Assert.False(binding.Bound);
        Assert.Equal(QuestStateMachine.SetActiveKey, binding.Key);

        Assert.False(machine.Apply(Quest, QuestNative.SetActive));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
        Assert.Equal(1, machine.Refused);
    }

    [Fact]
    public void SetStageNeverReliesOnAnUnprovenTimingRule()
    {
        var machine = new QuestStateMachine(Gate());
        machine.Apply(Quest, QuestNative.Start);

        Assert.False(machine.Apply(Quest, QuestNative.SetStage, 20));
        Assert.Equal(0, machine.StageOf(Quest));
        Assert.Equal(QuestState.Running, machine.StateOf(Quest));
        Assert.Equal(1, machine.Refused);
    }

    [Fact]
    public void SetStageAppliesOnceTheTimingIsProven()
    {
        var gate = Gate((QuestStageDispatch.TimingKey, "PROVEN"));
        var machine = new QuestStateMachine(gate);

        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 20));
        Assert.Equal(20, machine.StageOf(Quest));
        Assert.Equal(1, machine.Applied);
    }

    [Fact]
    public void EveryTargetedNativeIsBoundOrBlockedByANamedKey()
    {
        var gate = Gate();
        var report = new QuestStateMachine(gate).Bind(Host(gate), new[] { Quest });

        Assert.Equal(Enum.GetValues<QuestNative>().Length, report.Natives.Count);
        Assert.All(report.Natives, native => Assert.False(string.IsNullOrWhiteSpace(native.Key)));
        Assert.All(report.Natives, native => Assert.False(string.IsNullOrWhiteSpace(native.Reason)));
        Assert.Equal(report.Natives.Count, report.Bound + report.Blocked);
        Assert.Equal(2, report.Blocked);
        Assert.Equal(new[] { QuestNative.SetActive, QuestNative.SetStage },
            report.Natives.Where(native => !native.Bound).Select(native => native.Native).OrderBy(native => native.ToString()).ToArray());
    }

    [Fact]
    public void TheNativeReportIsDeterministic()
    {
        var gate = Gate();
        var first = new QuestStateMachine(gate).Bind(Host(gate), new[] { Quest });
        var second = new QuestStateMachine(gate).Bind(Host(gate), new[] { Quest });

        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
    }
}
