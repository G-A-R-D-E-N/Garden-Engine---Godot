using System;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStageStateTests : HostFixture
{
    private const string Quest = "000800:Quests.esp";
    private const string Other = "000801:Quests.esp";

    private static EvidenceGate Gate() => TestHosts.Gate(
        new[] { QuestStateMachine.LifecycleKey, QuestRuntime.FragmentTimingKey }.Select(key => (key, "ENGINE_POLICY")).ToArray());

    private static QuestStartupFragment Fragment(int stage, int entry, string function) => new((ushort)stage, entry, "QF_Probe", function);

    // Detects: a SetStage that queues no fragment, queues one for another stage or quest, queues a done stage again, or queues out of log entry order; each bound pair must queue once, after its own stage.
    [Fact]
    public void SetStageQueuesItsFragmentsInLogEntryOrderOnce()
    {
        var machine = new QuestStateMachine(Gate());
        machine.BindFragments(Quest, new[] { Fragment(10, 1, "Second"), Fragment(20, 0, "Other"), Fragment(10, 0, "First") });
        Assert.Empty(machine.TakeQueuedFragments());
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.Equal(new[] { "First", "Second" }, machine.TakeQueuedFragments().Select(fragment => fragment.Function));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.Empty(machine.TakeQueuedFragments());
        Assert.True(machine.Apply(Other, QuestNative.SetStage, 10));
        Assert.Empty(machine.TakeQueuedFragments());
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 20));
        var queued = Assert.Single(machine.TakeQueuedFragments());
        Assert.Equal((Quest, 20, 0, "Other"), (queued.Quest, queued.Stage, queued.LogEntry, queued.Function));
    }

    // Detects: a Reset or a save restore that leaves stale fragments queued; a restore must run no fragment, and a Reset must let the stage fire again.
    [Fact]
    public void AResetOrARestoreDropsQueuedFragments()
    {
        var machine = new QuestStateMachine(Gate());
        machine.BindFragments(Quest, new[] { Fragment(10, 0, "First") });
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.True(machine.Apply(Quest, QuestNative.Reset));
        Assert.Empty(machine.TakeQueuedFragments());
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.Single(machine.TakeQueuedFragments());
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.Empty(machine.TakeQueuedFragments());
        Assert.True(machine.Apply(Quest, QuestNative.Reset));
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        machine.Restore(new QuestStateSnapshot(Array.Empty<QuestStateEntry>()));
        Assert.Empty(machine.TakeQueuedFragments());
    }

    // Invariant: a save taken between a stage being set and its fragments draining must keep them pending.
    [Fact]
    public void AStageWhoseFragmentsHaveNotRunIsSavedAsPendingAndFiresOnceAfterTheLoad()
    {
        var fragments = new[] { Fragment(10, 1, "Second"), Fragment(10, 0, "First") };
        var machine = new QuestStateMachine(Gate());
        machine.BindFragments(Quest, fragments);
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));

        var loaded = new QuestStateMachine(Gate());
        loaded.BindFragments(Quest, fragments);
        loaded.Restore(QuestStateSave.Read(QuestStateSave.Sections(machine)));

        Assert.True(loaded.StageDone(Quest, 10));
        Assert.Equal(new[] { "First", "Second" }, loaded.TakeQueuedFragments().Select(fragment => fragment.Function));
        Assert.Empty(loaded.TakeQueuedFragments());
        Assert.True(loaded.Apply(Quest, QuestNative.SetStage, 10));
        Assert.Empty(loaded.TakeQueuedFragments());

        machine.TakeQueuedFragments();
        var drained = new QuestStateMachine(Gate());
        drained.BindFragments(Quest, fragments);
        drained.Restore(QuestStateSave.Read(QuestStateSave.Sections(machine)));
        Assert.Empty(drained.TakeQueuedFragments());
    }

    // Invariant: a pending stage that is not done, or is listed twice, must be refused.
    [Theory]
    [InlineData("{\"version\":1,\"quests\":[{\"quest\":\"q\",\"done\":[1],\"pending\":[2]}]}")]
    [InlineData("{\"version\":1,\"quests\":[{\"quest\":\"q\",\"done\":[1],\"pending\":[1,1]}]}")]
    [InlineData("{\"version\":1,\"quests\":[{\"quest\":\"q\",\"done\":[1],\"pending\":\"1\"}]}")]
    public void AMalformedPendingListIsRefused(string json) => Assert.Throws<FormatException>(() => QuestStateSave.Deserialize(json));

    [Fact]
    public void StageDoneRemembersEveryStageEverSet()
    {
        var machine = new QuestStateMachine(Gate());
        Assert.False(machine.StageDone(Quest, 10));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 20));
        Assert.Equal(20, machine.StageOf(Quest));
        Assert.True(machine.StageDone(Quest, 10));
        Assert.True(machine.StageDone(Quest, 20));
        Assert.False(machine.StageDone(Quest, 15));
        Assert.False(machine.StageDone(Other, 10));
        Assert.Equal(2, machine.StageChanges);
    }

    [Fact]
    public void ResetClearsTheStageAndTheDoneSet()
    {
        var machine = new QuestStateMachine(Gate());
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        var before = machine.StageChanges;
        Assert.True(machine.Apply(Quest, QuestNative.Reset));
        Assert.Equal(0, machine.StageOf(Quest));
        Assert.False(machine.StageDone(Quest, 10));
        Assert.Equal(before + 1, machine.StageChanges);
    }

    [Fact]
    public void IsKnownCoversOwnersAndStagedQuestsOnly()
    {
        var gate = Gate();
        var machine = new QuestStateMachine(gate);
        Assert.False(machine.IsKnown(Quest));
        machine.Bind(Track(TestHosts.Create(HostKind.Radicle, gate)), new[] { Quest });
        Assert.True(machine.IsKnown(Quest));
        Assert.False(machine.IsKnown(Other));
        Assert.True(machine.Apply(Other, QuestNative.SetStage, 5));
        Assert.True(machine.IsKnown(Other));
    }

    [Fact]
    public void ASessionOwnsAStageMachineAndBindsOnlyWhenAsked()
    {
        var gate = Gate();
        var session = new GameSession(Track(TestHosts.Create(HostKind.Radicle, gate)), gate);
        Assert.False(session.QuestStages.IsKnown(Quest));
        Assert.Equal(0, gate.StopCount);
        session.BindQuestNatives(new[] { Quest });
        Assert.True(session.QuestStages.IsKnown(Quest));
        Assert.Equal(0, session.QuestStages.StageOf(Quest));
        Assert.Equal(0, session.QuestStages.StageChanges);
    }
}
