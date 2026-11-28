using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Scenes;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class P5ReviewFixTests
{
    private const string Walker = "A2:Test.esm";

    private static SceneDef NonIgnoringPackageDef() => new(SceneSaveLoadTests.SceneKey, SceneSaveLoadTests.Quest,
        new[] { new ScenePhaseDef(0, "p0", null, null), new ScenePhaseDef(1, "p1", null, null), new ScenePhaseDef(2, "p2", null, null) },
        new[]
        {
            new SceneActionDef("Walk", SceneActionKind.Package, 2, 0, 2, false, "Pkg", 0),
            new SceneActionDef("Say1", SceneActionKind.Dialogue, 1, 1, 1, false, "T1", 0),
            new SceneActionDef("Say2", SceneActionKind.Dialogue, 3, 2, 2, false, "T2", 0),
        }, null, 10);

    [Fact]
    public void APackageActionThatDoesNotIgnoreCompletionAndEndsInALaterPhaseIsReleasedNotLeaked()
    {
        var game = new SceneSaveLoadTests.Game(NonIgnoringPackageDef());
        game.Scenes.Start(SceneSaveLoadTests.SceneKey);

        game.Run(30);

        Assert.True(game.Scenes.ReportOf(SceneSaveLoadTests.SceneKey)!.Completed);
        Assert.Null(game.World.Overrides.Of(Walker));
        Assert.Equal(0, game.World.Overrides.Count);
    }

    [Fact]
    public void ACancelledSceneLeavesNoForcedPackageWhateverTheActionFlags()
    {
        var game = new SceneSaveLoadTests.Game(NonIgnoringPackageDef());
        game.Scenes.Start(SceneSaveLoadTests.SceneKey);
        game.Run(0.5);

        game.Scenes.StopAll();

        Assert.Equal(0, game.World.Overrides.Count);
    }

    [Fact]
    public void TwoScenesSharingAPackageOnOneActorEachHoldItUntilTheirOwnEnd()
    {
        var book = new PackageOverrideBook<string>();
        book.Begin("a", "P", "v");
        book.Begin("a", "P", "v");

        Assert.True(book.End("a", "P"));
        Assert.Equal("v", book.Of("a"));
        Assert.True(book.End("a", "P"));
        Assert.Null(book.Of("a"));
        Assert.False(book.End("a", "P"));
    }

    [Fact]
    public void AResumeThatThrowsIsRecordedKeptInTheSaveAndDoesNotStopTheQueue()
    {
        var game = new SceneSaveLoadTests.Game();
        var runtime = new SceneRuntime(key => key == "bad" ? throw new InvalidOperationException("boom") : SceneSaveLoadTests.Def(), game.World);
        var queue = new SceneResumeQueue();
        queue.Enqueue(new[] { new SceneRunState("bad", 1), new SceneRunState(SceneSaveLoadTests.SceneKey, 1) });

        var resumed = queue.Tick(0.1, runtime);

        Assert.Equal(1, resumed);
        Assert.Equal(0, queue.Pending);
        Assert.Contains(new SceneRunState("bad", 1), queue.States);
        Assert.Contains(queue.Refusals, text => text.Contains("bad") && text.Contains("boom"));
        queue.Clear();
        Assert.Empty(queue.States);
        Assert.Empty(queue.Refusals);
    }

    [Fact]
    public void ALoadClearsTheAliasFillCacheAsWellAsTheForcedOverrides()
    {
        var fills = 0;
        var aliases = new QuestAliasResolver(_ => { fills++; return new[] { new AliasFillDef("q", 1, true, "t") }; }, _ => "live");
        Assert.Equal("live", aliases.Resolve("q", 1));
        Assert.Equal("live", aliases.Resolve("q", 1));
        Assert.Equal(1, fills);

        SceneStateSave.Restore(SceneStateSnapshot.Empty, null, aliases, null);
        aliases.Resolve("q", 1);

        Assert.Equal(2, fills);
    }
}
