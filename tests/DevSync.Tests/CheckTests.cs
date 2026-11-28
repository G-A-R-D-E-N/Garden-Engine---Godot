using GardenDevLauncher;
using Xunit;

namespace DevSync.Tests;

public class CheckTests : RepoFixture
{
    private static string Subject(string line) => line[(line.IndexOf(' ') + 1)..];

    [Fact]
    public void RefusedExceptionCarriesItsMessage()
    {
        var error = new RefusedException("stop");
        Assert.Equal("stop", error.Message);
        Assert.IsAssignableFrom<Exception>(error);
    }

    [Fact]
    public void DirtyFileComparesByStatusAndPath()
    {
        Assert.Equal(new DirtyFile("M", "a.txt"), new DirtyFile("M", "a.txt"));
        Assert.NotEqual(new DirtyFile("M", "a.txt"), new DirtyFile("A", "a.txt"));
    }

    [Fact]
    public void BaseRefDefaultsToOriginMain() => Assert.Equal("origin/main", Engine().BaseRef);

    [Fact]
    public void BaseRefUsesTheGivenRemoteAndBranch() =>
        Assert.Equal("upstream/trunk", Engine(remote: "upstream", baseBranch: "trunk").BaseRef);

    [Fact]
    public void IsRepositoryAcceptsTheWorkingTreeAndASubdirectory()
    {
        Directory.CreateDirectory(LocalFile("folder"));
        Assert.True(SyncEngine.IsRepository(Local));
        Assert.True(SyncEngine.IsRepository(LocalFile("folder")));
    }

    [Fact]
    public void IsRepositoryRejectsAPlainDirectoryAndAMissingOne()
    {
        var plain = Path.Combine(Root, "plain");
        Directory.CreateDirectory(plain);
        Assert.False(SyncEngine.IsRepository(plain));
        Assert.False(SyncEngine.IsRepository(Path.Combine(Root, "missing")));
    }

    [Fact]
    public void CheckOnACleanCheckoutReportsUpToDate()
    {
        var outcome = Engine().Check();

        Assert.Equal("up to date with main", outcome.Summary);
        Assert.True(outcome.Snapshot.UpToDate);
        Assert.Equal("work", outcome.Snapshot.Branch);
        Assert.Equal(0, outcome.Snapshot.Behind);
        Assert.Equal(0, outcome.Snapshot.Ahead);
        Assert.Empty(outcome.Snapshot.Dirty);
        Assert.Equal(
            new[] { "fetching origin main…", "branch work (0 behind main, 0 ahead)", "uncommitted work: none" },
            Log);
    }

    [Fact]
    public void CheckSeesMainWhenTheCloneRefspecIsNarrow()
    {
        Git(Local, "config", "remote.origin.fetch", "+refs/heads/other:refs/remotes/origin/other");
        PushToMain("Fix the water level");

        var outcome = Engine().Check();

        Assert.Equal("1 behind main, 0 uncommitted file(s)", outcome.Summary);
        Assert.False(outcome.Snapshot.UpToDate);
    }

    [Fact]
    public void CheckReportsCommitsThatLandedOnMainAndBranchOnlyCommits()
    {
        PushToMain("Fix the water level");
        Write("mine.txt", "mine\n");
        CommitLocal("My own work");

        var snapshot = Engine().Check().Snapshot;

        Assert.Equal(1, snapshot.Behind);
        Assert.Equal(1, snapshot.Ahead);
        Assert.Equal(new[] { "Fix the water level" }, snapshot.BehindSubjects.Select(Subject));
        Assert.Equal(new[] { "My own work" }, snapshot.AheadSubjects.Select(Subject));
        Assert.Contains("new on main:", Log);
        Assert.Contains("only on this branch:", Log);
        Assert.Contains("branch work (1 behind main, 1 ahead)", Log);
        Assert.Matches(@"^  [0-9a-f]+ Fix the water level$", Assert.Single(Log, l => l.EndsWith("Fix the water level")));
    }

    [Fact]
    public void CheckClassifiesEachKindOfUncommittedFile()
    {
        Write("c.txt", "c\n");
        CommitLocal("Add c");
        Write("a.txt", "one\nedit\n");
        File.Delete(LocalFile("c.txt"));
        Write("mm.txt", "staged\n");
        Git(Local, "add", "mm.txt");
        Write("mm.txt", "staged\nunstaged\n");
        Write("staged.txt", "s\n");
        Git(Local, "add", "staged.txt");
        Write("u.txt", "u\n");

        var outcome = Engine().Check();

        Assert.Equal(
            new[]
            {
                new DirtyFile("M", "a.txt"),
                new DirtyFile("D", "c.txt"),
                new DirtyFile("AM", "mm.txt"),
                new DirtyFile("A", "staged.txt"),
                new DirtyFile("??", "u.txt"),
            },
            outcome.Snapshot.Dirty);
        Assert.Equal("0 behind main, 5 uncommitted file(s)", outcome.Summary);
        Assert.Contains("uncommitted work: 5 file(s)", Log);
        Assert.Contains("  M  a.txt", Log);
        Assert.Contains("  AM mm.txt", Log);
        Assert.Contains("  ?? u.txt", Log);
    }

    [Fact]
    public void CheckListsFilesInUntrackedDirectoriesIndividually()
    {
        Write("dir/one.txt", "1\n");
        Write("dir/two.txt", "2\n");

        Assert.Equal(
            new[] { new DirtyFile("??", "dir/one.txt"), new DirtyFile("??", "dir/two.txt") },
            Engine().Check().Snapshot.Dirty);
    }

    [Fact]
    public void CheckIgnoresIgnoredFiles()
    {
        IgnoreLocally("private.cfg");
        Write("private.cfg", "secret\n");

        var outcome = Engine().Check();

        Assert.Empty(outcome.Snapshot.Dirty);
        Assert.Equal("up to date with main", outcome.Summary);
    }

    [Fact]
    public void CheckListsOnlyTwentyDirtyFilesAndCountsTheRest()
    {
        for (var i = 0; i < 25; i++) Write($"f{i:D2}.txt", "x\n");

        var outcome = Engine().Check();

        Assert.Equal(25, outcome.Snapshot.Dirty.Count);
        Assert.Contains("uncommitted work: 25 file(s)", Log);
        Assert.Equal(20, Log.Count(l => l.StartsWith("  ?? f")));
        Assert.Contains("  ?? f19.txt", Log);
        Assert.DoesNotContain("  ?? f20.txt", Log);
        Assert.Contains("  … 5 more", Log);
    }

    [Fact]
    public void CheckListsOnlyTwentyIncomingCommitsNewestFirst()
    {
        for (var i = 0; i < 22; i++) PushToMain($"Commit {i}", $"m{i}.txt");

        var snapshot = Engine().Check().Snapshot;

        Assert.Equal(22, snapshot.Behind);
        Assert.Equal(20, snapshot.BehindSubjects.Count);
        Assert.Equal("Commit 21", Subject(snapshot.BehindSubjects[0]));
        Assert.Equal("Commit 2", Subject(snapshot.BehindSubjects[19]));
    }

    [Fact]
    public void CheckOnADetachedHeadReportsNoBranch()
    {
        Git(Local, "switch", "--detach");

        var snapshot = Engine().Check().Snapshot;

        Assert.Null(snapshot.Branch);
        Assert.Contains("detached HEAD (0 behind main, 0 ahead)", Log);
    }

    [Fact]
    public void CheckResolvesTheWorkingTreeRootFromASubdirectory()
    {
        Directory.CreateDirectory(LocalFile("folder"));
        Write("top.txt", "t\n");

        var snapshot = Engine(LocalFile("folder")).Check().Snapshot;

        Assert.Equal(new[] { new DirtyFile("??", "top.txt") }, snapshot.Dirty);
    }

    [Fact]
    public void CheckTracksAnotherBaseBranch()
    {
        Git(Seed, "switch", "-c", "rel");
        Write("rel.txt", "r\n", Seed);
        Git(Seed, "add", "--all");
        Git(Seed, "commit", "--no-gpg-sign", "-m", "Release work");
        Git(Seed, "push", "origin", "rel:release");

        var outcome = Engine(baseBranch: "release").Check();

        Assert.Equal("1 behind release, 0 uncommitted file(s)", outcome.Summary);
        Assert.Contains("new on release:", Log);
        Assert.Contains("fetching origin release…", Log);
    }

    [Fact]
    public void CheckRefusesAMissingDirectory()
    {
        var missing = Path.Combine(Root, "missing");
        var error = Assert.Throws<RefusedException>(() => Engine(missing).Check());
        Assert.Equal($"{Path.GetFullPath(missing)} does not exist", error.Message);
    }

    [Fact]
    public void CheckRefusesADirectoryThatIsNotAWorkingTree()
    {
        var plain = Path.Combine(Root, "plain");
        Directory.CreateDirectory(plain);
        var error = Assert.Throws<RefusedException>(() => Engine(plain).Check());
        Assert.Equal($"{Path.GetFullPath(plain)} is not a git working tree", error.Message);
    }

    [Fact]
    public void CheckRefusesWhenTheBaseBranchDoesNotExistOnTheRemote()
    {
        var error = Assert.Throws<RefusedException>(() => Engine(baseBranch: "nope").Check());
        Assert.StartsWith("git fetch --prune origin +refs/heads/nope:refs/remotes/origin/nope failed:", error.Message);
    }
}
