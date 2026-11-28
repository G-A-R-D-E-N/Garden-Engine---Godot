using System;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStateMachineEdgeTests
{
    private const string Quest = "000800:Quests.esp";

    private static EvidenceGate Gate(params string[] proven) =>
        TestHosts.Gate(proven.Select(key => (key, "ENGINE_POLICY")).ToArray());

    private static QuestStateMachine Open() =>
        new(Gate(QuestStateMachine.LifecycleKey, QuestRuntime.FragmentTimingKey));

    [Fact]
    public void SetActiveFlagsTheQuestWithoutStartingOrStoppingIt()
    {
        var machine = new QuestStateMachine(Gate(QuestStateMachine.LifecycleKey, QuestStateMachine.SetActiveKey));
        Assert.True(machine.Apply(Quest, QuestNative.SetActive, active: true));
        Assert.True(machine.IsActive(Quest));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
        Assert.Equal(0, machine.StageChanges);

        Assert.True(machine.Apply(Quest, QuestNative.SetActive, active: false));
        Assert.False(machine.IsActive(Quest));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
    }

    [Fact]
    public void TheActiveFlagSurvivesEveryLifecycleNativeAndSetStage()
    {
        var machine = new QuestStateMachine(Gate(QuestStateMachine.LifecycleKey, QuestStateMachine.SetActiveKey, QuestRuntime.FragmentTimingKey));
        Assert.True(machine.Apply(Quest, QuestNative.SetActive, active: true));
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.True(machine.Apply(Quest, QuestNative.Stop));
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.CompleteQuest));
        Assert.True(machine.Apply(Quest, QuestNative.Reset));
        Assert.True(machine.IsActive(Quest));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
    }

    [Fact]
    public void TheActiveFlagRoundTripsThroughCaptureAndTheSaveSection()
    {
        var machine = new QuestStateMachine(Gate(QuestStateMachine.LifecycleKey, QuestStateMachine.SetActiveKey));
        Assert.True(machine.Apply(Quest, QuestNative.SetActive, active: true));
        var entry = Assert.Single(machine.Capture().Quests);
        Assert.True(entry.Active);
        Assert.Null(entry.State);

        var sections = QuestStateSave.Sections(machine);
        var fresh = new QuestStateMachine(Gate(QuestStateMachine.LifecycleKey));
        fresh.Restore(QuestStateSave.Read(sections));
        Assert.True(fresh.IsActive(Quest));
        Assert.Equal(QuestState.NotStarted, fresh.StateOf(Quest));
        Assert.Equal(sections[QuestStateSave.SectionName], QuestStateSave.Sections(fresh)[QuestStateSave.SectionName]);

        fresh.Restore(QuestStateSnapshot.Empty);
        Assert.False(fresh.IsActive(Quest));
    }

    [Fact]
    public void ASaveWrittenBeforeTheActiveFlagRestoresInactive()
    {
        var old = "{\"version\":1,\"quests\":[{\"quest\":\"" + Quest + "\",\"state\":\"Running\",\"stage\":10,\"done\":[10]}]}";
        var machine = new QuestStateMachine(Gate(QuestStateMachine.LifecycleKey));
        machine.Restore(QuestStateSave.Deserialize(old));
        Assert.Equal(QuestState.Running, machine.StateOf(Quest));
        Assert.False(machine.IsActive(Quest));
        Assert.Throws<FormatException>(() => QuestStateSave.Deserialize(old.Replace("\"done\"", "\"active\":1,\"done\"")));
    }

    [Fact]
    public void AnUnprovenTimingKeyStillAppliesSetStageAndCountsOneStop()
    {
        var gate = Gate(QuestStateMachine.LifecycleKey);
        var machine = new QuestStateMachine(gate);
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 30));
        Assert.Equal(30, machine.StageOf(Quest));
        Assert.Equal(1, gate.StopCount);
        Assert.Equal(1, gate.Stops[QuestRuntime.FragmentTimingKey]);
    }

    [Fact]
    public void AnUnprovenLifecycleKeyStillStartsTheQuestAndCountsOneStop()
    {
        var gate = Gate(QuestRuntime.FragmentTimingKey);
        var machine = new QuestStateMachine(gate);
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.Equal(QuestState.Running, machine.StateOf(Quest));
        Assert.Equal(1, gate.Stops[QuestStateMachine.LifecycleKey]);
    }

    [Fact]
    public void StartIsRefusedWhileRunningAndAfterCompletion()
    {
        var machine = Open();
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.False(machine.Apply(Quest, QuestNative.Start));
        Assert.Equal(QuestState.Running, machine.StateOf(Quest));
        Assert.True(machine.Apply(Quest, QuestNative.CompleteQuest));
        Assert.False(machine.Apply(Quest, QuestNative.Start));
        Assert.Equal(QuestState.Completed, machine.StateOf(Quest));
    }

    [Fact]
    public void StopAndCompleteAreRefusedFromStopped()
    {
        var machine = Open();
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.Stop));
        Assert.False(machine.Apply(Quest, QuestNative.Stop));
        Assert.False(machine.Apply(Quest, QuestNative.CompleteQuest));
        Assert.Equal(QuestState.Stopped, machine.StateOf(Quest));
    }

    [Fact]
    public void ResetClearsStageAndDoneSetAndMovesTheRevision()
    {
        var machine = Open();
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 20));
        Assert.Equal(2, machine.StageChanges);

        Assert.True(machine.Apply(Quest, QuestNative.Reset));

        Assert.Equal(3, machine.StageChanges);
        Assert.Equal(0, machine.StageOf(Quest));
        Assert.False(machine.StageDone(Quest, 10));
        Assert.False(machine.StageDone(Quest, 20));
        var entry = Assert.Single(machine.Capture().Quests);
        Assert.Equal(QuestState.NotStarted, entry.State);
        Assert.Null(entry.Stage);
        Assert.Empty(entry.Done);
    }

    [Fact]
    public void SetStageKeepsEveryStageEverReachedInTheDoneSet()
    {
        var machine = Open();
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 40));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.Equal(10, machine.StageOf(Quest));
        Assert.True(machine.StageDone(Quest, 40));
        Assert.True(machine.StageDone(Quest, 10));
        Assert.False(machine.StageDone(Quest, 20));
        Assert.True(machine.IsKnown(Quest));
        Assert.Equal(new[] { 10, 40 }, machine.Capture().Quests.Single().Done);
    }

    [Fact]
    public void ASecondSetStageOfTheSameStageStillMovesTheRevision()
    {
        var machine = Open();
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.Equal(2, machine.StageChanges);
    }
}
