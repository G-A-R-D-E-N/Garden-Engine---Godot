using OpenCommonwealth.Services.Papyrus.Scenes;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Papyrus.Tests;

[Trait("Category", "RealData")]
public sealed class SceneSaveLoadRealTests
{
    private const string Scene = "03467E:Fallout4.esm";
    private const string Quest = "027411:Fallout4.esm";

    [RealDataFact]
    public void TheGoodneighborStartSceneSavedMidwayResumesItsPhaseAndSetsQuestStageTenOnce()
    {
        using var env = RadicleEnvironment.Load(new[] { Path.Combine(RegistryCorpusRealTests.DataDirectory!, "Fallout4.esm") });
        var def = RadicleSceneSource.Read(env, Scene)!;
        Assert.Equal(Quest, def.Quest);
        var before = new SceneSaveLoadTests.Game(def);
        before.Machine.Apply(Quest, OpenCommonwealth.Services.Papyrus.Quest.QuestNative.SetStage, 5);
        Assert.Equal(SceneStartOutcome.Started, before.Scenes.Start(Scene).Outcome);
        for (var i = 0; i < 400 && before.Scenes.Capture().FirstOrDefault()?.Phase < 5; i++) before.Run(0.25);
        var saved = Assert.Single(before.Scenes.Capture());
        Assert.Equal(5, saved.Phase);
        Assert.DoesNotContain(10, before.World.Stages);
        var spokenBefore = before.World.Spoken.Count;
        var save = before.Save();

        var after = new SceneSaveLoadTests.Game(def);
        after.Load(save);
        after.Run(120);

        Assert.False(after.Scenes.IsPlaying(Scene));
        Assert.True(after.Scenes.ReportOf(Scene)!.Completed);
        Assert.Equal(1, after.World.Stages.Count(stage => stage == 10));
        var startPhaseLines = def.Actions.Count(a => a.Kind == SceneActionKind.Dialogue && a.StartPhase == 5);
        var laterLines = def.Actions.Count(a => a.Kind == SceneActionKind.Dialogue && a.StartPhase > 5);
        Assert.Equal(startPhaseLines + laterLines, after.World.Spoken.Count);
        Assert.True(spokenBefore > 0);
        Assert.Equal(4, def.Actions.Count(a => a.Kind == SceneActionKind.Package && a.IgnoreForCompletion));
        Assert.Equal(def.Actions.Count(a => a.Kind == SceneActionKind.Package && a.StartPhase <= 5 && a.EndPhase >= 5), after.World.Packages.Count);
    }

    [RealDataFact]
    public void TheCompletedGoodneighborStartSceneIsNotReplayedByALoad()
    {
        using var env = RadicleEnvironment.Load(new[] { Path.Combine(RegistryCorpusRealTests.DataDirectory!, "Fallout4.esm") });
        var def = RadicleSceneSource.Read(env, Scene)!;
        var before = new SceneSaveLoadTests.Game(def);
        before.Scenes.Start(Scene);
        before.Run(200);
        Assert.True(before.Scenes.ReportOf(Scene)!.Completed);

        var after = new SceneSaveLoadTests.Game(def);
        after.Load(before.Save());
        after.Run(200);

        Assert.Empty(after.World.Spoken);
        Assert.Empty(after.World.Packages);
        Assert.False(after.Scenes.IsPlaying(Scene));
    }

    [RealDataFact]
    public void EveryPackageActionOfTheGoodneighborStartSceneIgnoresCompletion()
    {
        using var env = RadicleEnvironment.Load(new[] { Path.Combine(RegistryCorpusRealTests.DataDirectory!, "Fallout4.esm") });
        var def = RadicleSceneSource.Read(env, Scene)!;

        var packages = def.Actions.Where(a => a.Kind == SceneActionKind.Package).ToList();

        Assert.NotEmpty(packages);
        Assert.All(packages, action => Assert.True(action.IgnoreForCompletion));
    }
}
