using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Scenes;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class PackageOverrideTests
{
    private const string Walker = "A2:Test.esm";

    [Fact]
    public void TheNewestOverrideWinsAndEndingItBringsBackTheOneUnderIt()
    {
        var book = new PackageOverrideBook<string>();
        book.Begin("a", "P1", "one");
        book.Begin("a", "P2", "two");

        Assert.Equal("two", book.Of("a"));
        Assert.True(book.End("a", "P2"));
        Assert.Equal("one", book.Of("a"));
        Assert.True(book.End("a", "P1"));
        Assert.Null(book.Of("a"));
        Assert.False(book.End("a", "P1"));
        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void BeginningTheSamePackageAgainKeepsOneEntryAndEveryChangeMovesTheRevision()
    {
        var book = new PackageOverrideBook<string>();
        var start = book.Revision;
        book.Begin("a", "P1", "one");
        book.Begin("a", "P1", "one");
        Assert.Equal(1, book.Count);
        Assert.Equal(start + 2, book.Revision);
        book.End("a", "missing");
        Assert.Equal(start + 2, book.Revision);
        book.Clear();
        Assert.Equal(start + 3, book.Revision);
        book.Clear();
        Assert.Equal(start + 3, book.Revision);
        Assert.Null(book.Of("a"));
    }

    [Fact]
    public void AnActorWithoutAnOverrideHasNone()
    {
        var book = new PackageOverrideBook<string>();
        book.Begin("a", "P1", "one");

        Assert.Null(book.Of("b"));
    }

    [Fact]
    public void ASceneForcesItsPackageOnTheActorWhileTheActionRunsAndReleasesItWhenTheSceneEnds()
    {
        var game = new SceneSaveLoadTests.Game();
        game.Machine.Apply(SceneSaveLoadTests.Quest, OpenCommonwealth.Services.Papyrus.Quest.QuestNative.SetStage, 5);
        game.Scenes.Start(SceneSaveLoadTests.SceneKey);
        game.Run(0.5);

        Assert.Equal("Pkg", game.World.Overrides.Of(Walker));
        Assert.Null(game.World.Overrides.Of("A1:Test.esm"));

        game.Run(10);

        Assert.False(game.Scenes.IsPlaying(SceneSaveLoadTests.SceneKey));
        Assert.Null(game.World.Overrides.Of(Walker));
    }

    [Fact]
    public void CancellingTheSceneReleasesTheForcedPackage()
    {
        var game = new SceneSaveLoadTests.Game();
        game.Scenes.Start(SceneSaveLoadTests.SceneKey);
        game.Run(0.5);
        Assert.Equal("Pkg", game.World.Overrides.Of(Walker));

        game.Scenes.StopAll();

        Assert.Null(game.World.Overrides.Of(Walker));
    }

    [Fact]
    public void ALoadDropsTheForcedPackageAndAResumedSceneForcesItAgainOnce()
    {
        var before = new SceneSaveLoadTests.Game();
        before.Machine.Apply(SceneSaveLoadTests.Quest, OpenCommonwealth.Services.Papyrus.Quest.QuestNative.SetStage, 5);
        before.Scenes.Start(SceneSaveLoadTests.SceneKey);
        before.Run(1.5);
        var save = before.Save();

        var after = new SceneSaveLoadTests.Game();
        after.World.Overrides.Begin(Walker, "Stale", "Stale");
        after.Load(save);

        Assert.Null(after.World.Overrides.Of(Walker));
        after.Run(0.5);
        Assert.Equal("Pkg", after.World.Overrides.Of(Walker));
        Assert.Equal(1, after.World.Overrides.Count);

        after.Load(save);
        Assert.Null(after.World.Overrides.Of(Walker));
        after.Run(0.5);
        Assert.Equal(1, after.World.Overrides.Count);
    }

    [Fact]
    public void ASceneParkedByALeftCellReleasesTheForcedPackage()
    {
        var game = new SceneSaveLoadTests.Game();
        game.Scenes.Start(SceneSaveLoadTests.SceneKey);
        game.Run(0.5);
        var state = game.Scenes.Capture().Single();

        Assert.True(game.Scenes.Stop(state.Scene));

        Assert.Null(game.World.Overrides.Of(Walker));
        game.Resumes.Enqueue(new[] { state });
        game.Run(0.5);
        Assert.Equal("Pkg", game.World.Overrides.Of(Walker));
    }
}
