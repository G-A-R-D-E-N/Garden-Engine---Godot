using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestLateStartTests : HostFixture
{
    private const string Quest = "000800:QuestProbe.esp";

    private static readonly ScriptSpec[] Scripts =
    {
        TestHosts.Stub("ScriptObject"),
        TestHosts.Stub("Form"),
        TestHosts.Stub("QuestScript", null,
            "Int Property Count Auto",
            "Float Property SignedZero Auto",
            "Bool Property Flag Auto",
            "String Property Text Auto",
            "Function OnInit()\nEndFunction"),
        TestHosts.Stub("Quest", null,
            "Function Start() native", "Function Stop() native", "Function Reset() native", "Function CompleteQuest() native",
            "Function SetActive(Bool abActive) native", "Bool Function SetStage(Int aiStage) native"),
        TestHosts.Stub("Recorder", null, "Function Hit(Int stage, Int entry) global native"),
        TestHosts.Stub("QF_Probe", "Quest", "Function Fragment_Stage_0010_Item_00()\n  Recorder.Hit(10, 0)\nEndFunction"),
    };

    private GameSession Session(bool startGame, out List<int> hits)
    {
        var input = QuestStartupBindingTests.Input();
        var fragments = new[] { new QuestStartupFragment(10, 0, "QF_Probe", "Fragment_Stage_0010_Item_00") };
        input = input with
        {
            StartGameEnabled = startGame,
            Metadata = input.Metadata! with
            {
                ScriptProperties = input.Metadata.ScriptProperties.Take(4).ToArray(),
                FragmentScript = "QF_Probe",
                Fragments = fragments,
            },
            Fragments = fragments.Length,
            Stages = new[] { new QuestStartupStage(10, false, 1) },
        };
        var gate = QuestStartupBindingTests.Gate();
        var session = new GameSession(Track(TestHosts.Create(HostKind.Radicle, gate, Scripts)), gate, new[] { input });
        var seen = new List<int>();
        session.Host.RegisterNative(new NativeId("Recorder", "Hit"), args =>
        {
            seen.Add((int)args[0].Integer);
            return PapyrusValue.None;
        });
        hits = seen;
        return session;
    }

    // Detects: a quest that Quest.Start moved to Running after startup but whose scripts are never attached or added to the started list, so its stage fragments are refused; the next drain must attach it.
    [Fact]
    public void ALaterQuestStartAttachesTheQuestScriptsOnTheNextDrain()
    {
        var session = Session(false, out _);
        Assert.True(session.StartNewGame().Applied);
        Assert.DoesNotContain(Quest, session.Quests.Started);
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.Start));
        session.RunQueuedFragments();
        Assert.Contains(Quest, session.Quests.Started);
        Assert.Contains(session.Quests.InstancesFor(Quest), instance => instance.ScriptName == "QuestScript");
        Assert.Empty(session.LateStartFailures);
    }

    // Detects: a quest that was never started being attached by the drain; only a Running quest must be started late.
    [Fact]
    public void AQuestThatWasNotStartedStaysUnattachedAfterADrain()
    {
        var session = Session(false, out _);
        Assert.True(session.StartNewGame().Applied);
        session.RunQueuedFragments();
        Assert.DoesNotContain(Quest, session.Quests.Started);
    }

    // Detects: a stage fragment queued for a late-started quest that is refused for good instead of running once the quest scripts are attached; it must run once.
    [Fact]
    public void AStageFragmentQueuedBeforeTheLateStartRunsOnceTheQuestIsStarted()
    {
        var session = Session(false, out var hits);
        Assert.True(session.StartNewGame().Applied);
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.Start));
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.SetStage, 10));
        Assert.Equal(1, session.RunQueuedFragments());
        Assert.Equal(new[] { 10 }, hits);
        Assert.Equal(0, session.FragmentsRefused);
    }

    // Detects: a fragment that fails before it runs being dropped while the stage stays done, so setting the stage again cannot queue it; it must be retried.
    [Fact]
    public void AFragmentThatCouldNotStartYetIsRetriedOnTheNextDrain()
    {
        var session = Session(true, out var hits);
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.Start));
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.SetStage, 10));
        Assert.Equal(0, session.RunQueuedFragments());
        Assert.Equal(1, session.FragmentsRetried);
        Assert.Equal(0, session.FragmentsRefused);
        Assert.True(session.StartNewGame().Applied);
        Assert.Equal(1, session.RunQueuedFragments());
        Assert.Equal(new[] { 10 }, hits);
        Assert.Empty(session.FailedFragments);
    }

    // Detects: a fragment that never becomes runnable being retried forever or silently; it must end in the failed list after the attempt limit.
    [Fact]
    public void AFragmentThatNeverRunsIsReportedAfterTheAttemptLimit()
    {
        var session = Session(true, out var hits);
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.Start));
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.SetStage, 10));
        for (var attempt = 0; attempt < GameSession.MaxFragmentAttempts; attempt++) session.RunQueuedFragments();
        Assert.Equal(1, session.FragmentsRefused);
        Assert.Equal(GameSession.MaxFragmentAttempts - 1, session.FragmentsRetried);
        Assert.Single(session.FailedFragments);
        Assert.Equal(0, session.RunQueuedFragments());
        Assert.Empty(hits);
    }
}
