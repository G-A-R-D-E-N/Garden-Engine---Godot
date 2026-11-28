using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStateSaveTests
{
    private const string Quest = "000800:Quests.esp";
    private const string Other = "000801:Quests.esp";
    private const string Third = "000802:Quests.esp";

    private static EvidenceGate Gate() => TestHosts.Gate(
        new[] { QuestStateMachine.LifecycleKey, QuestRuntime.FragmentTimingKey }.Select(key => (key, "ENGINE_POLICY")).ToArray());

    private static QuestStateMachine Played()
    {
        var machine = new QuestStateMachine(Gate());
        Assert.True(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 10));
        Assert.True(machine.Apply(Quest, QuestNative.SetStage, 20));
        Assert.True(machine.Apply(Other, QuestNative.Start));
        Assert.True(machine.Apply(Other, QuestNative.CompleteQuest));
        Assert.True(machine.Apply(Third, QuestNative.SetStage, 5));
        return machine;
    }

    private static string Doc(string quests, string version = "1") => "{\"version\":" + version + ",\"quests\":[" + quests + "]}";

    [Fact]
    public void AnEmptyMachineWritesNoSection()
    {
        Assert.Empty(QuestStateSave.Sections(new QuestStateMachine(Gate())));
        Assert.Empty(QuestStateSave.Sections(null));
    }

    [Fact]
    public void StatesStagesAndDoneSetsRoundTripThroughTheSection()
    {
        var sections = QuestStateSave.Sections(Played());
        var snapshot = QuestStateSave.Read(sections);
        var fresh = new QuestStateMachine(Gate());
        fresh.Restore(snapshot);

        Assert.Equal(QuestState.Running, fresh.StateOf(Quest));
        Assert.Equal(20, fresh.StageOf(Quest));
        Assert.True(fresh.StageDone(Quest, 10));
        Assert.True(fresh.StageDone(Quest, 20));
        Assert.False(fresh.StageDone(Quest, 15));
        Assert.Equal(QuestState.Completed, fresh.StateOf(Other));
        Assert.Equal(QuestState.NotStarted, fresh.StateOf(Third));
        Assert.Equal(5, fresh.StageOf(Third));
        Assert.True(fresh.StageDone(Third, 5));
        Assert.Equal(sections[QuestStateSave.SectionName], QuestStateSave.Sections(fresh)[QuestStateSave.SectionName]);
    }

    [Fact]
    // Constraint: the NewLine pin only has teeth on Windows, where the default newline is CRLF.
    public void TheSectionTextIsPinned()
    {
        var text = Encoding.UTF8.GetString(QuestStateSave.Sections(Played())[QuestStateSave.SectionName]);
        const string expected = "{\n  \"version\": 1,\n  \"quests\": [\n" +
            "    {\n      \"quest\": \"000800:Quests.esp\",\n      \"state\": \"Running\",\n      \"stage\": 20,\n      \"done\": [\n        10,\n        20\n      ]\n    },\n" +
            "    {\n      \"quest\": \"000801:Quests.esp\",\n      \"state\": \"Completed\",\n      \"done\": []\n    },\n" +
            "    {\n      \"quest\": \"000802:Quests.esp\",\n      \"stage\": 5,\n      \"done\": [\n        5\n      ]\n    }\n  ]\n}";
        Assert.Equal(expected, text);
    }

    [Fact]
    public void RestoreReplacesWhatWasThereWithoutTheGate()
    {
        var machine = Played();
        var snapshot = machine.Capture();
        var target = new QuestStateMachine(Gate());
        Assert.True(target.Apply("000899:Quests.esp", QuestNative.Start));
        var revision = target.StageChanges;

        target.Restore(snapshot);

        Assert.Equal(QuestState.NotStarted, target.StateOf("000899:Quests.esp"));
        Assert.Equal(QuestState.Running, target.StateOf(Quest));
        Assert.Equal(revision + 1, target.StageChanges);
    }

    [Fact]
    public void RestoringAnEmptySnapshotResetsTheMachineAndStillMovesTheRevision()
    {
        var machine = Played();
        var revision = machine.StageChanges;
        machine.Restore(QuestStateSnapshot.Empty);
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Quest));
        Assert.Equal(0, machine.StageOf(Quest));
        Assert.False(machine.StageDone(Quest, 10));
        Assert.Equal(revision + 1, machine.StageChanges);
    }

    [Fact]
    public void ARestoredRunningQuestKeepsObeyingTheLifecycleRules()
    {
        var machine = new QuestStateMachine(Gate());
        machine.Restore(QuestStateSave.Read(QuestStateSave.Sections(Played())));
        Assert.False(machine.Apply(Quest, QuestNative.Start));
        Assert.True(machine.Apply(Quest, QuestNative.CompleteQuest));
        Assert.Equal(QuestState.Completed, machine.StateOf(Quest));
        Assert.False(machine.Apply(Other, QuestNative.Stop));
    }

    [Fact]
    public void ASaveWithoutTheSectionReadsAsEmpty()
    {
        Assert.True(QuestStateSave.Read(new Dictionary<string, byte[]> { ["npc.actors.json"] = new byte[] { 1 } }).IsEmpty);
    }

    [Theory]
    [InlineData("{\"version\":1,\"quests\":[]")]
    [InlineData("[]")]
    [InlineData("{\"quests\":[]}")]
    [InlineData("{\"version\":0,\"quests\":[]}")]
    [InlineData("{\"version\":2,\"quests\":[]}")]
    [InlineData("{\"version\":\"1\",\"quests\":[]}")]
    [InlineData("{\"version\":1,\"version\":1,\"quests\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"quests\":[]} x")]
    [InlineData("{\"version\":1,\"quests\":[]}{}")]
    [InlineData("{\"version\":1,\"quests\":[null]}")]
    public void ADocumentOutsideTheFormatIsRefused(string json)
    {
        Assert.Throws<FormatException>(() => QuestStateSave.Deserialize(json));
    }

    [Theory]
    [InlineData("{\"quest\":\"\",\"done\":[]}")]
    [InlineData("{\"done\":[]}")]
    [InlineData("{\"quest\":\"A\",\"state\":\"Paused\",\"done\":[]}")]
    [InlineData("{\"quest\":\"A\",\"state\":\"running\",\"done\":[]}")]
    [InlineData("{\"quest\":\"A\",\"state\":\"1\",\"done\":[]}")]
    [InlineData("{\"quest\":\"A\",\"state\":1,\"done\":[]}")]
    [InlineData("{\"quest\":\"A\",\"stage\":\"10\",\"done\":[]}")]
    [InlineData("{\"quest\":\"A\",\"stage\":1.5,\"done\":[]}")]
    [InlineData("{\"quest\":\"A\"}")]
    [InlineData("{\"quest\":\"A\",\"done\":[1,1]}")]
    [InlineData("{\"quest\":\"A\",\"done\":[\"1\"]}")]
    [InlineData("{\"quest\":\"A\",\"done\":[]},{\"quest\":\"A\",\"done\":[]}")]
    [InlineData("{\"quest\":\"A\",\"quest\":\"B\",\"done\":[]}")]
    [InlineData("{\"quest\":\"A\",\"done\":[],\"done\":[]}")]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("{\"quest\":\"A\",\"done\":[null]}")]
    [InlineData("{\"quest\":\"A\",\"stage\":3,\"done\":[]}")]
    [InlineData("{\"quest\":\"A\",\"stage\":3,\"done\":[4]}")]
    public void AnEntryOutsideTheFormatIsRefused(string quests)
    {
        Assert.Throws<FormatException>(() => QuestStateSave.Deserialize(Doc(quests)));
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        var json = "{\"version\":1,\"extra\":{\"a\":1},\"quests\":[{\"quest\":\"A\",\"note\":[1],\"state\":\"Running\",\"stage\":3,\"done\":[3]}]}";
        var entry = Assert.Single(QuestStateSave.Deserialize(json).Quests);
        Assert.Equal("A", entry.Quest);
        Assert.Equal(QuestState.Running, entry.State);
        Assert.Equal(3, entry.Stage);
        Assert.Equal(new[] { 3 }, entry.Done);
    }

    [Fact]
    public void ADoneSetWithoutAStageIsAccepted()
    {
        var entry = Assert.Single(QuestStateSave.Deserialize(Doc("{\"quest\":\"A\",\"done\":[4]}")).Quests);
        Assert.Null(entry.Stage);
        Assert.Equal(new[] { 4 }, entry.Done);
    }

    [Fact]
    public void RestoreRefusesAStageOutsideItsDoneSetAndChangesNothing()
    {
        var machine = Played();
        var before = QuestStateSave.Sections(machine)[QuestStateSave.SectionName];
        var revision = machine.StageChanges;
        var bad = new QuestStateSnapshot(new[] { new QuestStateEntry("A", QuestState.Running, 3, new[] { 4 }) });

        Assert.Throws<FormatException>(() => machine.Restore(bad));

        Assert.Equal(before, QuestStateSave.Sections(machine)[QuestStateSave.SectionName]);
        Assert.Equal(revision, machine.StageChanges);
    }

    [Fact]
    public void ABoundMachineDropsAQuestThatIsNotAKnownOwnerAndRestoresTheRest()
    {
        var machine = Played();
        using var host = new RadicleScriptHost(Gate(), Array.Empty<byte[]>());
        machine.Bind(host, new[] { Quest, Other });
        var revision = machine.StageChanges;
        var snapshot = new QuestStateSnapshot(new[]
        {
            new QuestStateEntry(Quest, QuestState.Running, 40, new[] { 40 }),
            new QuestStateEntry("000999:Quests.esp", QuestState.Running, 7, new[] { 7 }),
        });

        Assert.Equal(1, machine.Restore(snapshot));

        Assert.Equal(QuestState.Running, machine.StateOf(Quest));
        Assert.Equal(40, machine.StageOf(Quest));
        Assert.True(machine.StageDone(Quest, 40));
        Assert.Equal(QuestState.NotStarted, machine.StateOf(Other));
        Assert.Equal(QuestState.NotStarted, machine.StateOf("000999:Quests.esp"));
        Assert.Equal(0, machine.StageOf("000999:Quests.esp"));
        Assert.False(machine.StageDone("000999:Quests.esp", 7));
        Assert.Equal(revision + 1, machine.StageChanges);
    }

    [Fact]
    public void AnInvalidEntryIsStillRefusedBeforeAnyDropAndChangesNothing()
    {
        var machine = Played();
        using var host = new RadicleScriptHost(Gate(), Array.Empty<byte[]>());
        machine.Bind(host, new[] { Quest, Other });
        var before = QuestStateSave.Sections(machine)[QuestStateSave.SectionName];
        var revision = machine.StageChanges;
        var bad = new QuestStateSnapshot(new[]
        {
            new QuestStateEntry(Quest, QuestState.Running, null, Array.Empty<int>()),
            new QuestStateEntry("000999:Quests.esp", QuestState.Running, 3, new[] { 4 }),
        });

        Assert.Throws<FormatException>(() => machine.Restore(bad));

        Assert.Equal(before, QuestStateSave.Sections(machine)[QuestStateSave.SectionName]);
        Assert.Equal(revision, machine.StageChanges);
    }

    [Fact]
    public void AnUnboundMachineDropsNothing()
    {
        var machine = new QuestStateMachine(Gate());
        Assert.Equal(0, machine.Restore(machine.Capture()));
        Assert.Equal(0, machine.Restore(Played().Capture()));
        Assert.Equal(QuestState.Running, machine.StateOf(Quest));
        Assert.Equal(QuestState.Completed, machine.StateOf(Other));
    }

    [Fact]
    public void ANegativeStageIsKeptBecauseApplyAcceptsAnyInteger()
    {
        var machine = new QuestStateMachine(Gate());
        machine.Restore(QuestStateSave.Deserialize(Doc("{\"quest\":\"A\",\"stage\":-3,\"done\":[-3]}")));
        Assert.Equal(-3, machine.StageOf("A"));
        Assert.True(machine.StageDone("A", -3));
    }
}
