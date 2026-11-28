using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Dialogue;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Papyrus.Vmad;
using FormKey = Radicle.Interop.FormKey;
using DialogueInfo = Radicle.Interop.DialogueInfo;
using DialogueText = Radicle.Interop.DialogueText;
using Xunit;
using NameKind = Radicle.Interop.Native.NameKind;

namespace OpenCw.Papyrus.Tests;

public sealed class DialogueEffectsTests : HostFixture
{
    static readonly FormKey QuestKey = new("Quests.esp", 0x800);

    static EvidenceGate Gate() => TestHosts.Gate(
        new[] { QuestStateMachine.LifecycleKey, QuestRuntime.FragmentTimingKey }.Select(key => (key, "ENGINE_POLICY")).ToArray());

    static DialogueInfoData Info(InfoResult? result, FormKey? quest) =>
        new(new FormKey("Quests.esp", 0x900),
            new DialogueInfo(null, null, null, null, null, null, null, null, null, new DialogueText(NameKind.Absent, null, 0), [], []),
            [], null, [], quest, false, result);

    static (QuestStateMachine Machine, QuestStageEffects Effects) Setup(bool running = true)
    {
        var machine = new QuestStateMachine(Gate());
        machine.BindFragments(QuestKey.ToString(), [new QuestStartupFragment(30, 0, "QF_Probe", "Fragment_30")]);
        if (running)
            Assert.True(machine.Apply(QuestKey.ToString(), QuestNative.Start));
        return (machine, new QuestStageEffects(machine, () => (machine.TakeQueuedFragments().Count, 0)));
    }

    [Fact]
    public void AnOnEndStageSetsTheQuestStageAndRunsItsQuestFragments()
    {
        var (machine, effects) = Setup();
        var outcome = Assert.Single(effects.Run(Info(new InfoResult(null, 30, false), QuestKey), true));
        Assert.Equal(EffectStatus.Applied, outcome.Status);
        Assert.Contains("1 quest fragment(s) ran", outcome.Detail);
        Assert.Equal(30, machine.StageOf(QuestKey.ToString()));
    }

    [Fact]
    public void AnOnBeginStageIsNotRunAtTheEnd()
    {
        var (machine, effects) = Setup();
        Assert.Empty(effects.Run(Info(new InfoResult(30, null, false), QuestKey), true));
        Assert.Equal(0, machine.StageOf(QuestKey.ToString()));
    }

    [Fact]
    public void ANotRunningQuestRefusesTheStageAndChangesNothing()
    {
        var (machine, effects) = Setup(running: false);
        var outcome = Assert.Single(effects.Run(Info(new InfoResult(null, 30, false), QuestKey), true));
        Assert.Equal(EffectStatus.Refused, outcome.Status);
        Assert.Equal(0, machine.StageOf(QuestKey.ToString()));
    }

    [Fact]
    public void AStageWithoutAParentQuestIsRefused()
    {
        var (_, effects) = Setup();
        Assert.Equal(EffectStatus.Refused, Assert.Single(effects.Run(Info(new InfoResult(null, 30, false), null), true)).Status);
    }

    [Fact]
    public void AThrowingFragmentDrainIsAFailedOutcomeNotAnException()
    {
        var machine = new QuestStateMachine(Gate());
        Assert.True(machine.Apply(QuestKey.ToString(), QuestNative.Start));
        var effects = new QuestStageEffects(machine, () => throw new System.InvalidOperationException("drain broke"));
        var outcome = Assert.Single(effects.Run(Info(new InfoResult(null, 30, false), QuestKey), true));
        Assert.Equal(EffectStatus.Failed, outcome.Status);
        Assert.Contains("drain broke", outcome.Detail);
    }

    static InfoVmadParse Rows(params InfoFragmentRow[] rows) => new("F_Info", [], rows, null);

    static QuestStageEffects WithRunner(List<string> calls, bool completes = true) =>
        new(new QuestStateMachine(Gate()), () => (0, 0), (info, parse, row) =>
        {
            calls.Add($"{info} {row.Script}.{row.Function}");
            return (completes, completes ? "completed" : "the host refused it");
        });

    [Fact]
    public void AVmadThatWasNotReadIsRefusedAtTheEndAndNeverCountedAsApplied()
    {
        var (_, effects) = Setup();
        var outcome = Assert.Single(effects.Run(Info(new InfoResult(null, null, true), QuestKey), true));
        Assert.Equal(EffectStatus.Refused, outcome.Status);
        Assert.Equal(QuestStageEffects.FragmentNotRead, outcome.Detail);
    }

    [Fact]
    public void AnUnparseableVmadIsRefusedWithTheParserReason()
    {
        var (_, effects) = Setup();
        var parse = InfoVmadParse.Failed("the VMAD subrecord ends early at offset 9 of 9");
        var outcome = Assert.Single(effects.Run(Info(new InfoResult(null, null, true, null, parse), QuestKey), true));
        Assert.Equal(EffectStatus.Refused, outcome.Status);
        Assert.Contains("ends early", outcome.Detail);
    }

    [Fact]
    public void TheBeginRowRunsAtBeginAndTheEndRowAtEndEachThroughTheHost()
    {
        var calls = new List<string>();
        var effects = WithRunner(calls);
        var result = new InfoResult(null, null, true, null, Rows(new InfoFragmentRow(false, "F_Info", "Fragment_Begin"), new InfoFragmentRow(true, "F_Info", "Fragment_End")));
        Assert.Equal(EffectStatus.Applied, Assert.Single(effects.Run(Info(result, QuestKey), false)).Status);
        Assert.EndsWith("F_Info.Fragment_Begin", Assert.Single(calls));
        Assert.Equal(EffectStatus.Applied, Assert.Single(effects.Run(Info(result, QuestKey), true)).Status);
        Assert.Equal(2, calls.Count);
        Assert.EndsWith("F_Info.Fragment_End", calls[1]);
    }

    [Fact]
    public void AnInfoWithOnlyAnEndRowRunsNothingAtBegin()
    {
        var calls = new List<string>();
        var result = new InfoResult(null, null, true, null, Rows(new InfoFragmentRow(true, "F_Info", "Fragment_End")));
        Assert.Empty(WithRunner(calls).Run(Info(result, QuestKey), false));
        Assert.Empty(calls);
    }

    [Fact]
    public void AFragmentTheHostRefusesIsRefusedWithItsReasonNotApplied()
    {
        var result = new InfoResult(null, null, true, null, Rows(new InfoFragmentRow(true, "F_Info", "Fragment_End")));
        var outcome = Assert.Single(WithRunner([], completes: false).Run(Info(result, QuestKey), true));
        Assert.Equal(EffectStatus.Refused, outcome.Status);
        Assert.Equal("the host refused it", outcome.Detail);
    }

    [Fact]
    public void AnUnreadableVmadIsReportedAtBeginAndNotAgainAtEnd()
    {
        var (_, effects) = Setup();
        var result = new InfoResult(null, null, true, null, InfoVmadParse.Failed("the VMAD subrecord ends early"));
        var begin = Assert.Single(effects.Run(Info(result, QuestKey), false));
        Assert.Equal(EffectStatus.Refused, begin.Status);
        Assert.Contains("ends early", begin.Detail);
        Assert.Empty(effects.Run(Info(result, QuestKey), true));
    }

    [Fact]
    public void AnInfoWithoutAResultDoesNothing()
    {
        var (_, effects) = Setup();
        Assert.Empty(effects.Run(Info(null, QuestKey), true));
        Assert.Empty(effects.Run(Info(InfoResult.None, QuestKey), false));
    }
}
