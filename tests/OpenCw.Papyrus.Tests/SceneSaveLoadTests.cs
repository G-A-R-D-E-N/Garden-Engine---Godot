using System.Text;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SceneSaveLoadTests
{
    internal const string Quest = "027411:Test.esm";
    internal const string SceneKey = "03467E:Test.esm";

    internal sealed class World : ISceneWorld
    {
        private readonly QuestStateMachine _machine;

        public World(QuestStateMachine machine) => _machine = machine;

        public Dictionary<int, string> Actors { get; } = new() { [1] = "A1:Test.esm", [2] = "A2:Test.esm", [3] = "A3:Test.esm" };

        public List<string> Spoken { get; } = new();

        public List<string> Packages { get; } = new();

        public List<int> Stages { get; } = new();

        public PackageOverrideBook<string> Overrides { get; } = new();

        public string? ResolveActor(string quest, int alias) => Actors.GetValueOrDefault(alias);

        public SpeechResult Speak(string context, string topic, string? actor)
        {
            Spoken.Add(topic);
            return new SpeechResult(SpeechStatus.Spoken, 1.0, "");
        }

        public void StopSpeech(string context, string actor) { }

        public PackageResult BeginPackage(string context, string package, string? actor)
        {
            Packages.Add(package);
            if (actor is not null) Overrides.Begin(actor, package, package);
            return new PackageResult(true, "");
        }

        public void EndPackage(string package, string? actor)
        {
            if (actor is not null) Overrides.End(actor, package);
        }

        public bool SetStage(string quest, int stage)
        {
            Stages.Add(stage);
            return _machine.Apply(quest, QuestNative.SetStage, stage);
        }
    }

    internal static SceneDef Def() => new(SceneKey, Quest,
        new[] { new ScenePhaseDef(0, "p0", null, null), new ScenePhaseDef(1, "p1", 6, null), new ScenePhaseDef(2, "p2", null, 7), new ScenePhaseDef(3, "p3", null, null) },
        new[]
        {
            new SceneActionDef("Say0", SceneActionKind.Dialogue, 1, 0, 0, false, "T0", 0),
            new SceneActionDef("Say1", SceneActionKind.Dialogue, 2, 1, 1, false, "T1", 0),
            new SceneActionDef("Say2", SceneActionKind.Dialogue, 3, 2, 2, false, "T2", 0),
            new SceneActionDef("Say3", SceneActionKind.Dialogue, 1, 3, 3, false, "T3", 0),
            new SceneActionDef("Walk", SceneActionKind.Package, 2, 0, 2, true, "Pkg", 0),
        }, null, 10);

    internal sealed class Game
    {
        public Game(SceneDef? def = null)
        {
            Machine = new QuestStateMachine(TestHosts.Gate((QuestStateMachine.LifecycleKey, "ENGINE_POLICY"), (QuestRuntime.FragmentTimingKey, "ENGINE_POLICY")));
            Machine.BindFragments(Quest, new[] { new QuestStartupFragment(5, 0, "QF_Start", "Fragment_Stage_0005"), new QuestStartupFragment(10, 0, "QF_Start", "Fragment_Stage_0010") });
            Machine.Apply(Quest, QuestNative.Start);
            World = new World(Machine);
            Aliases = new QuestAliasResolver(_ => Array.Empty<AliasFillDef>(), _ => null);
            var scene = def ?? Def();
            Scenes = new SceneRuntime(key => key == scene.Key ? scene : null, World);
        }

        public QuestStateMachine Machine { get; }

        public World World { get; }

        public QuestAliasResolver Aliases { get; }

        public SceneRuntime Scenes { get; }

        public SceneResumeQueue Resumes { get; } = new();

        public void Run(double seconds, double step = 0.25)
        {
            for (var elapsed = 0.0; elapsed < seconds; elapsed += step)
            {
                Resumes.Tick(step, Scenes);
                Scenes.Tick(step);
            }
        }

        public IReadOnlyDictionary<string, byte[]> Save()
        {
            var sections = QuestStateSave.Sections(Machine).ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var (name, payload) in SceneStateSave.Sections(Scenes.Capture().Concat(Resumes.States), Aliases)) sections[name] = payload;
            return sections;
        }

        public void Load(IReadOnlyDictionary<string, byte[]> sections)
        {
            var stages = QuestStateSave.Read(sections);
            var scenes = SceneStateSave.Read(sections);
            Machine.Restore(stages);
            World.Overrides.Clear();
            SceneStateSave.Restore(scenes, Scenes, Aliases, Resumes);
        }
    }

    private static Game ThroughPhaseOne()
    {
        var game = new Game();
        game.Machine.Apply(Quest, QuestNative.SetStage, 5);
        Assert.Equal(SceneStartOutcome.Started, game.Scenes.Start(SceneKey).Outcome);
        game.Run(1.5);
        Assert.Equal(new[] { new SceneRunState(SceneKey, 1) }, game.Scenes.Capture());
        return game;
    }

    [Fact]
    public void SaveMidSceneThenLoadResumesTheSavedPhaseAndSetsTheEndStageOnce()
    {
        var before = ThroughPhaseOne();
        var save = before.Save();
        Assert.Contains(SceneStateSave.SectionName, save.Keys);
        Assert.Equal(new[] { 6 }, before.World.Stages);

        var after = new Game();
        after.Load(save);
        after.Run(8);

        Assert.Equal(new[] { "T1", "T2", "T3" }, after.World.Spoken);
        Assert.Equal(new[] { 7, 10 }, after.World.Stages);
        Assert.Equal(new[] { "Pkg" }, after.World.Packages);
        Assert.False(after.Scenes.IsPlaying(SceneKey));
        Assert.True(after.Scenes.ReportOf(SceneKey)!.Completed);
        Assert.Equal(10, after.Machine.StageOf(Quest));
    }

    [Fact]
    public void AResumedPhaseDoesNotSetItsStartStageAgainSoTheStageNeverMovesBackwards()
    {
        var before = ThroughPhaseOne();
        before.Machine.Apply(Quest, QuestNative.SetStage, 8);

        var after = new Game();
        after.Load(before.Save());
        after.Run(0.5);

        Assert.Equal(8, after.Machine.StageOf(Quest));
        Assert.DoesNotContain(6, after.World.Stages);
    }

    [Fact]
    public void ASceneThatCompletedBeforeTheSaveIsNotResumedOrRestartedByTheLoad()
    {
        var before = new Game();
        before.Machine.Apply(Quest, QuestNative.SetStage, 5);
        before.Scenes.Start(SceneKey);
        before.Run(10);
        Assert.True(before.Scenes.ReportOf(SceneKey)!.Completed);
        before.Machine.TakeQueuedFragments();
        var save = before.Save();
        Assert.False(SceneStateSave.Read(save).Runs.Any());

        var after = new Game();
        after.Load(save);
        after.Run(40);

        Assert.Empty(after.World.Spoken);
        Assert.Empty(after.World.Stages);
        Assert.False(after.Scenes.IsPlaying(SceneKey));
        Assert.Null(after.Scenes.ReportOf(SceneKey));
        Assert.Equal(0, after.Machine.QueuedFragments);
        Assert.True(after.Machine.StageDone(Quest, 10));
        Assert.True(after.Machine.Apply(Quest, QuestNative.SetStage, 5));
        Assert.Equal(0, after.Machine.QueuedFragments);
    }

    [Fact]
    public void LoadingTheSameSaveTwiceLeavesOneRunAndOneEndStage()
    {
        var save = ThroughPhaseOne().Save();
        var game = new Game();

        game.Load(save);
        game.Run(0.5);
        game.Load(save);
        game.Run(8);

        Assert.Equal(1, game.World.Stages.Count(stage => stage == 10));
        Assert.Equal(new[] { "T1", "T1", "T2", "T3" }, game.World.Spoken);
    }

    [Fact]
    public void LoadingASaveWithNoSceneStateStopsTheLiveSceneAndDropsItsAliasesWithoutSettingAStage()
    {
        var empty = new Game().Save();
        var game = ThroughPhaseOne();
        game.Aliases.ForceRefTo(Quest, 1, "F1:Test.esm");

        game.Load(empty);
        game.Run(8);

        Assert.False(game.Scenes.IsPlaying(SceneKey));
        Assert.Null(game.Scenes.ReportOf(SceneKey));
        Assert.Empty(game.Aliases.Capture());
        Assert.Equal(0, game.Resumes.Pending);
        Assert.DoesNotContain(10, game.World.Stages);
    }

    [Fact]
    public void ForcedAliasesAndForcedClearsSurviveALoadIntoAFreshProcess()
    {
        var before = new Game();
        before.Aliases.ForceRefTo(Quest, 1, "F1:Test.esm");
        before.Aliases.Clear(Quest, 2);
        before.Aliases.ForceRefTo("000800:Other.esp", 4, "F2:Test.esm");

        var after = new Game();
        after.Aliases.ForceRefTo(Quest, 9, "STALE:Test.esm");
        after.Load(before.Save());

        Assert.Equal("F1:Test.esm", after.Aliases.Resolve(Quest, 1));
        Assert.Null(after.Aliases.Resolve(Quest, 2));
        Assert.Equal("F2:Test.esm", after.Aliases.Resolve("000800:Other.esp", 4));
        Assert.Null(after.Aliases.Resolve(Quest, 9));
        Assert.Equal(3, after.Aliases.Capture().Count);
    }

    [Fact]
    public void AFragmentQueuedAtTheSaveRunsOnceAfterTheLoadAndNeverQueuesAgain()
    {
        var before = new Game();
        Assert.True(before.Machine.Apply(Quest, QuestNative.SetStage, 5));
        Assert.Equal(1, before.Machine.QueuedFragments);
        var save = before.Save();

        var after = new Game();
        after.Load(save);

        var queued = Assert.Single(after.Machine.TakeQueuedFragments());
        Assert.Equal((Quest, 5, "Fragment_Stage_0005"), (queued.Quest, queued.Stage, queued.Function));
        Assert.True(after.Machine.Apply(Quest, QuestNative.SetStage, 5));
        Assert.Equal(0, after.Machine.QueuedFragments);
    }

    [Fact]
    public void AFragmentAlreadyRunAtTheSaveIsNotQueuedByTheLoad()
    {
        var before = new Game();
        before.Machine.Apply(Quest, QuestNative.SetStage, 5);
        before.Machine.TakeQueuedFragments();
        var save = before.Save();

        var after = new Game();
        after.Load(save);

        Assert.Equal(0, after.Machine.QueuedFragments);
        Assert.DoesNotContain("queued", Encoding.UTF8.GetString(save[QuestStateSave.SectionName]));
    }

    [Fact]
    public void LeavingTheCellAndReturningResumesTheParkedSceneOnceItsActorsAreBack()
    {
        var game = ThroughPhaseOne();
        var parked = game.Scenes.Capture();
        Assert.True(game.Scenes.Stop(SceneKey));
        game.World.Actors.Clear();
        var spokenBefore = game.World.Spoken.Count;

        game.Resumes.Enqueue(parked);
        game.Run(10);
        Assert.Equal(1, game.Resumes.Pending);
        Assert.Equal(spokenBefore, game.World.Spoken.Count);
        Assert.DoesNotContain(10, game.World.Stages);

        game.World.Actors[1] = "A1:Test.esm";
        game.World.Actors[2] = "A2:Test.esm";
        game.World.Actors[3] = "A3:Test.esm";
        game.Run(8);

        Assert.Equal(0, game.Resumes.Pending);
        Assert.Equal(new[] { "T0", "T1", "T1", "T2", "T3" }, game.World.Spoken);
        Assert.Equal(1, game.World.Stages.Count(stage => stage == 10));
        Assert.Equal(1, game.World.Stages.Count(stage => stage == 6));
    }

    [Fact]
    public void AParkedSceneWhoseActorsNeverReturnResumesAfterTheWaitSoItsEndStageIsNotLostForever()
    {
        var game = ThroughPhaseOne();
        var parked = game.Scenes.Capture();
        game.Scenes.Stop(SceneKey);
        game.World.Actors.Clear();
        game.Resumes.Enqueue(parked);

        game.Run(SceneResumeQueue.MaxWaitSeconds - 1);
        Assert.Equal(1, game.Resumes.Pending);
        game.Run(10);

        Assert.Equal(0, game.Resumes.Pending);
        Assert.Equal(1, game.World.Stages.Count(stage => stage == 10));
        Assert.True(game.Scenes.ReportOf(SceneKey)!.EndStageSet);
    }

    [Fact]
    public void ASceneLeftRunningWhileItsActorsAreGoneFinishesWithNoLineSpoken()
    {
        var game = ThroughPhaseOne();
        game.World.Actors.Clear();
        var spoken = game.World.Spoken.Count;

        game.Run(5);

        Assert.Equal(spoken, game.World.Spoken.Count);
        Assert.Equal(1, game.World.Stages.Count(stage => stage == 10));
    }

    [Fact]
    public void ASavedRunQueuedButNotBegunStartsFromItsBeginningAfterTheLoad()
    {
        var before = new Game();
        before.Scenes.Start(SceneKey);
        var save = before.Save();
        Assert.Equal(new[] { new SceneRunState(SceneKey, -1) }, SceneStateSave.Read(save).Runs);

        var after = new Game();
        after.Load(save);
        after.Run(8);

        Assert.Equal(new[] { "T0", "T1", "T2", "T3" }, after.World.Spoken);
        Assert.Equal(1, after.World.Stages.Count(stage => stage == 10));
    }

    [Fact]
    public void ASavedSceneUnknownOrOutOfRangeInThisPluginSetIsRefusedByNameAndNotRun()
    {
        var game = new Game();
        game.Resumes.Enqueue(new[] { new SceneRunState("000111:Gone.esm", 2), new SceneRunState(SceneKey, 99) });

        game.Run(1);

        Assert.Equal(0, game.Resumes.Pending);
        Assert.False(game.Scenes.IsPlaying(SceneKey));
        Assert.Equal(2, game.Resumes.Refusals.Count);
        Assert.Contains("not a known scene", game.Resumes.Refusals[0]);
        Assert.Contains("no phase 99", game.Resumes.Refusals[1]);
    }

    [Fact]
    public void ASavePutsASceneStillWaitingToResumeBackIntoTheSave()
    {
        var game = new Game();
        game.Resumes.Enqueue(new[] { new SceneRunState(SceneKey, 2) });

        Assert.Equal(new[] { new SceneRunState(SceneKey, 2) }, SceneStateSave.Read(game.Save()).Runs);
    }

    [Fact]
    public void ALoadDropsARunStillWaitingToResumeFromAnEarlierLoad()
    {
        var game = new Game();
        game.Resumes.Enqueue(new[] { new SceneRunState(SceneKey, 2) });

        game.Load(new Game().Save());
        game.Run(40);

        Assert.Equal(0, game.Resumes.Pending);
        Assert.False(game.Scenes.IsPlaying(SceneKey));
        Assert.Empty(game.World.Spoken);
    }

    [Fact]
    public void TheSceneStateSectionRoundTripsAndRefusesMalformedInput()
    {
        var snapshot = new SceneStateSnapshot(new[] { new SceneRunState(SceneKey, 3) }, new[] { new ForcedAlias(Quest, 1, "F1:Test.esm"), new ForcedAlias(Quest, 2, null) });

        var read = SceneStateSave.Deserialize(SceneStateSave.Serialize(snapshot));

        Assert.Equal(snapshot.Runs, read.Runs);
        Assert.Equal(snapshot.Aliases, read.Aliases);
        Assert.Throws<FormatException>(() => SceneStateSave.Deserialize("{}"));
        Assert.Throws<FormatException>(() => SceneStateSave.Deserialize("{\"version\":2,\"runs\":[],\"aliases\":[]}"));
        Assert.Throws<FormatException>(() => SceneStateSave.Deserialize("{\"version\":1,\"runs\":[{\"scene\":\"A\",\"phase\":-2}],\"aliases\":[]}"));
        Assert.Throws<FormatException>(() => SceneStateSave.Deserialize("{\"version\":1,\"runs\":[{\"scene\":\"A\",\"phase\":1},{\"scene\":\"A\",\"phase\":2}],\"aliases\":[]}"));
        Assert.Throws<FormatException>(() => SceneStateSave.Deserialize("{\"version\":1,\"runs\":[],\"aliases\":[{\"quest\":\"Q\",\"alias\":1},{\"quest\":\"Q\",\"alias\":1}]}"));
    }

    [Fact]
    public void AQueuedFragmentForAStageThatIsNotDoneRefusesTheQuestSection()
    {
        var bad = new QuestStateSnapshot(
            new[] { new QuestStateEntry(Quest, QuestState.Running, 5, new[] { 5 }) },
            new[] { new QueuedFragment(Quest, 9, 0, "QF_Start", "Fragment_Stage_0009") });

        Assert.Throws<FormatException>(() => QuestStateSave.Deserialize(QuestStateSave.Serialize(bad)));
    }

    [Fact]
    public void ASceneWithAPackageActionSpanningTheSavedPhaseAppliesThatPackageAgain()
    {
        var game = new Game();
        game.Resumes.Enqueue(new[] { new SceneRunState(SceneKey, 2) });

        game.Run(0.5);

        Assert.Equal(new[] { "Pkg" }, game.World.Packages);
        Assert.Equal(new[] { "T2" }, game.World.Spoken);
    }
}
