using GardenDevLauncher;
using Xunit;

namespace DevSync.Tests;

public class SyncTests : RepoFixture
{
    private const string WorkCommit = "Save local work before syncing with main";
    private const string MainWork =
        "local main has 1 commit(s) not on origin/main; they are saved, " +
        "move them onto a branch and sync that branch: git switch -c my-work";

    [Fact]
    public void SyncSavesUncommittedWorkAndMergesMain()
    {
        PushToMain("Fix the water level");
        Write("work.txt", "local work\n");
        Write("a.txt", "one\nlocal edit\n");

        var outcome = Engine().Sync();

        Assert.Equal("merged origin/main into work", outcome.Summary);
        Assert.Equal("work", outcome.Snapshot.Branch);
        Assert.Equal(0, outcome.Snapshot.Behind);
        Assert.Equal(2, outcome.Snapshot.Ahead);
        Assert.Empty(outcome.Snapshot.Dirty);
        Assert.Equal("", Git(Local, "status", "--porcelain"));
        Assert.Equal("local work\n", File.ReadAllText(LocalFile("work.txt")));
        Assert.Equal("one\nlocal edit\n", File.ReadAllText(LocalFile("a.txt")));
        Assert.Equal("remote\n", File.ReadAllText(LocalFile("b.txt")));
        Assert.Equal(WorkCommit, Git(Local, "log", "-1", "--format=%s", "HEAD^1").Trim());
        Assert.Equal("2", Git(Local, "rev-list", "--count", "origin/main..HEAD").Trim());
        Assert.Contains("merged origin/main into work; 2 ahead, 0 behind", Log);
        Assert.Matches(
            @"^saved 2 file\(s\) as [0-9a-f]+ ""Save local work before syncing with main""$",
            Assert.Single(Log, l => l.StartsWith("saved 2")));
    }

    [Fact]
    public void SyncMergesMainWithAMergeCommitWhenTheTreeIsClean()
    {
        PushToMain("Fix the water level");

        var outcome = Engine().Sync();

        Assert.Equal("merged origin/main into work", outcome.Summary);
        Assert.Equal(1, outcome.Snapshot.Ahead);
        Assert.Equal(0, outcome.Snapshot.Behind);
        Assert.Contains("saved: nothing to save", Log);
        Assert.Contains("merged origin/main into work; 1 ahead, 0 behind", Log);
        Assert.Equal(2, Git(Local, "rev-list", "--parents", "-n", "1", "HEAD").Trim().Split(' ').Length - 1);
    }

    [Fact]
    public void SyncOnACleanUpToDateBranchChangesNothing()
    {
        var before = Head();

        var outcome = Engine().Sync();

        Assert.Equal("already on top of main", outcome.Summary);
        Assert.Equal(before, Head());
        Assert.Contains("saved: nothing to save", Log);
        Assert.Contains("already on top of main", Log);
    }

    [Fact]
    public void SyncOnlySavesWhenTheBranchIsAlreadyOnTopOfMain()
    {
        Write("work.txt", "local work\n");

        var outcome = Engine().Sync();

        Assert.Equal("saved 1 file(s)", outcome.Summary);
        Assert.Equal(1, outcome.Snapshot.Ahead);
        Assert.Equal(WorkCommit, Git(Local, "log", "-1", "--format=%s").Trim());
    }

    [Fact]
    public void SyncPreservesBothStagedAndUnstagedVersions()
    {
        PushToMain("Advance main");
        Write("a.txt", "staged version\n");
        Git(Local, "add", "a.txt");
        Write("a.txt", "unstaged version\n");

        Engine().Sync();

        Assert.Equal("unstaged version\n", File.ReadAllText(LocalFile("a.txt")));
        Assert.Equal("staged version\n", Git(Local, "show", "HEAD~2:a.txt"));
        Assert.Equal("unstaged version\n", Git(Local, "show", "HEAD:a.txt"));
        Assert.Equal("Save staged work before syncing with main", Git(Local, "log", "-1", "--format=%s", "HEAD~2").Trim());
        Assert.Equal(WorkCommit, Git(Local, "log", "-1", "--format=%s", "HEAD~1").Trim());
    }

    [Fact]
    public void SyncRespectsARejectingCommitHookWithoutLosingWork()
    {
        var hooks = Path.Combine(GitDir(), "hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\nexit 1\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        PushToMain("Advance main");
        Write("a.txt", "staged version\n");
        Git(Local, "add", "a.txt");
        Write("a.txt", "unstaged version\n");
        var before = Head();

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.StartsWith("git commit -m Save staged work before syncing with main failed:", error.Message);
        Assert.Equal(before, Head());
        Assert.Equal("staged version\n", Git(Local, "show", ":a.txt"));
        Assert.Equal("unstaged version\n", File.ReadAllText(LocalFile("a.txt")));
    }

    [Fact]
    public void SyncConflictIsRefusedBeforeMergingAndKeepsTheSavedCommit()
    {
        PushToMain("Rewrite the first file", "a.txt", "one\nremote edit\n");
        Write("a.txt", "one\nlocal edit\n");

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Matches(@"^merge was not started; saved work remains at refs/devsync/backups/[0-9a-f]{32}\. ", error.Message);
        Assert.Contains("a.txt", error.Message);
        Assert.False(File.Exists(Path.Combine(GitDir(), "MERGE_HEAD")));
        Assert.Equal("", Git(Local, "status", "--porcelain"));
        Assert.Equal(WorkCommit, Git(Local, "log", "-1", "--format=%s").Trim());
        Assert.Equal(Head(), Git(Local, "for-each-ref", "--format=%(objectname)", "refs/devsync/backups/").Trim());
        Assert.Equal("one\nlocal edit\n", File.ReadAllText(LocalFile("a.txt")));
    }

    [Fact]
    public void SyncRefusesToOverwriteAnIgnoredLocalFile()
    {
        IgnoreLocally("private.cfg");
        Write("private.cfg", "private local data\n");
        PushToMain("Add configuration", "private.cfg", "default\n");
        var before = Head();

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal("ignored local path may be overwritten: private.cfg; move it before syncing", error.Message);
        Assert.Equal("private local data\n", File.ReadAllText(LocalFile("private.cfg")));
        Assert.Equal(before, Head());
    }

    [Fact]
    public void SyncFromASubdirectoryStillProtectsIgnoredFilesAtTheRoot()
    {
        IgnoreLocally("private.cfg");
        Write("private.cfg", "private local data\n");
        Directory.CreateDirectory(LocalFile("folder"));
        PushToMain("Add configuration", "private.cfg", "default\n");

        var error = Assert.Throws<RefusedException>(() => Engine(LocalFile("folder")).Sync());

        Assert.Equal("ignored local path may be overwritten: private.cfg; move it before syncing", error.Message);
        Assert.Equal("private local data\n", File.ReadAllText(LocalFile("private.cfg")));
    }

    [Fact]
    public void SyncRefusesToReplaceAnIgnoredDirectoryWithAFile()
    {
        IgnoreLocally("private-dir/");
        Write("private-dir/local.txt", "private local data\n");
        PushToMain("Add file", "private-dir", "default\n");

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal("ignored local path may be overwritten: private-dir/; move it before syncing", error.Message);
        Assert.Equal("private local data\n", File.ReadAllText(LocalFile("private-dir/local.txt")));
    }

    [Fact]
    public void SyncRefusesToReplaceAnIgnoredFileWithADirectory()
    {
        IgnoreLocally("private-node");
        Write("private-node", "private local data\n");
        PushToMain("Add directory", "private-node/child.txt", "default\n");

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal("ignored local path may be overwritten: private-node; move it before syncing", error.Message);
        Assert.Equal("private local data\n", File.ReadAllText(LocalFile("private-node")));
    }

    [Fact]
    public void SyncComparesIgnoredPathsIgnoringCase()
    {
        IgnoreLocally("Private.cfg");
        Write("Private.cfg", "private local data\n");
        PushToMain("Add configuration", "private.cfg", "default\n");

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal("ignored local path may be overwritten: Private.cfg; move it before syncing", error.Message);
    }

    [Fact]
    public void SyncLeavesUnrelatedIgnoredFilesAlone()
    {
        IgnoreLocally("local-cache/");
        Write("local-cache/local.txt", "private local data\n");
        PushToMain("Advance main");

        var outcome = Engine().Sync();

        Assert.Equal("merged origin/main into work", outcome.Summary);
        Assert.Equal("private local data\n", File.ReadAllText(LocalFile("local-cache/local.txt")));
    }

    [Fact]
    public void SyncRefusesBranchMergeOptionsThatChangeTheMerge()
    {
        Git(Local, "config", "--", "branch.work.mergeOptions", "--not-a-real-merge-option");
        PushToMain("Advance main");
        Write("work.txt", "local work\n");
        var before = Head();

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal("branch merge options are configured; sync manually to preserve them", error.Message);
        Assert.Equal(before, Head());
        Assert.Equal("local work\n", File.ReadAllText(LocalFile("work.txt")));
    }

    [Fact]
    public void SyncIgnoresABlankBranchMergeOptionsValue()
    {
        Git(Local, "config", "branch.work.mergeOptions", "");
        PushToMain("Advance main");

        Assert.Equal("merged origin/main into work", Engine().Sync().Summary);
    }

    [Fact]
    public void SyncPreservesExistingMergeHistory()
    {
        Git(Local, "switch", "--create", "side");
        Write("side.txt", "side\n");
        CommitLocal("Side work");
        Git(Local, "switch", "work");
        Git(Local, "merge", "--no-ff", "--no-edit", "side");
        var originalMerge = Head();
        PushToMain("Advance main");
        Write("work.txt", "uncommitted\n");

        Engine().Sync();

        Git(Local, "merge-base", "--is-ancestor", originalMerge, "HEAD");
        Assert.Equal("uncommitted\n", File.ReadAllText(LocalFile("work.txt")));
    }

    [Fact]
    public void SyncRefusesADirtySubmodule()
    {
        var sub = Path.Combine(Root, "sub");
        Git(Root, "init", "--initial-branch=main", sub);
        Write("sub.txt", "original\n", sub);
        Git(sub, "add", "sub.txt");
        Git(sub, "commit", "--no-gpg-sign", "-m", "Initial submodule");
        Git(Local, "-c", "protocol.file.allow=always", "submodule", "add", sub, "nested");
        Git(Local, "commit", "--no-gpg-sign", "-m", "Add submodule");
        Write("nested/sub.txt", "uncommitted submodule work\n");
        var before = Head();

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal("a submodule contains uncommitted work; save it before syncing", error.Message);
        Assert.Equal(before, Head());
        Assert.Equal("uncommitted submodule work\n", File.ReadAllText(LocalFile("nested/sub.txt")));
    }

    [Theory]
    [InlineData("rebase-merge", "a rebase is in progress")]
    [InlineData("rebase-apply", "a rebase or am is in progress")]
    [InlineData("MERGE_HEAD", "a merge is in progress")]
    [InlineData("CHERRY_PICK_HEAD", "a cherry-pick is in progress")]
    [InlineData("REVERT_HEAD", "a revert is in progress")]
    [InlineData("sequencer", "a multi-commit operation is in progress")]
    public void SyncRefusesWhileAnOperationIsInProgress(string marker, string message)
    {
        if (marker.EndsWith("_HEAD")) File.WriteAllText(Path.Combine(GitDir(), marker), Head() + "\n");
        else Directory.CreateDirectory(Path.Combine(GitDir(), marker));
        Write("work.txt", "local work\n");
        var before = Head();

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal($"{message}; finish or abort it first", error.Message);
        Assert.Equal(before, Head());
        Assert.Equal("?? work.txt", Git(Local, "status", "--porcelain").Trim());
    }

    [Fact]
    public void SyncReportsTheFirstInProgressMarkerWhenSeveralExist()
    {
        Directory.CreateDirectory(Path.Combine(GitDir(), "sequencer"));
        Directory.CreateDirectory(Path.Combine(GitDir(), "rebase-merge"));

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal("a rebase is in progress; finish or abort it first", error.Message);
    }

    [Fact]
    public void SyncRefusesADetachedHead()
    {
        Git(Local, "switch", "--detach");

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal("HEAD is detached; check out a branch before syncing", error.Message);
    }

    [Fact]
    public void SyncFastForwardsMain()
    {
        Git(Local, "switch", "main");
        PushToMain("Fix the water level");

        var outcome = Engine().Sync();

        Assert.Equal("fast-forwarded main", outcome.Summary);
        Assert.Equal(Git(Local, "rev-parse", "origin/main").Trim(), Head());
        Assert.Equal(0, outcome.Snapshot.Behind);
        Assert.Contains("fast-forwarded main to origin/main", Log);
        Assert.Equal("remote\n", File.ReadAllText(LocalFile("b.txt")));
    }

    [Fact]
    public void SyncOnMainRefusesToFastForwardOverWorkItJustSaved()
    {
        Git(Local, "switch", "main");
        PushToMain("Fix the water level");
        Write("work.txt", "local work\n");

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal(MainWork, error.Message);
        Assert.Equal(WorkCommit, Git(Local, "log", "-1", "--format=%s").Trim());
        Assert.Equal("", Git(Local, "status", "--porcelain"));
    }

    [Fact]
    public void SyncRefusesLocalCommitsOnMainInsteadOfRewritingThem()
    {
        Git(Local, "switch", "main");
        Write("main-work.txt", "on main\n");
        CommitLocal("Work done straight on main");
        PushToMain("Fix the water level");
        var before = Head();

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal(MainWork, error.Message);
        Assert.Equal(before, Head());
    }

    [Fact]
    public void SyncOnMainRefusesSquashConfiguration()
    {
        Git(Local, "switch", "main");
        Git(Local, "config", "--", "branch.main.mergeOptions", "--squash");
        PushToMain("Advance main");
        var before = Head();

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal("branch merge options are configured; sync manually to preserve them", error.Message);
        Assert.Equal(before, Head());
        Assert.Equal("", Git(Local, "status", "--porcelain"));
    }

    [Fact]
    public void SyncOnMainRefusesAnIgnoredFileCollision()
    {
        Git(Local, "switch", "main");
        IgnoreLocally("private.cfg");
        Write("private.cfg", "private local data\n");
        PushToMain("Add configuration", "private.cfg", "default\n");
        var before = Head();

        var error = Assert.Throws<RefusedException>(() => Engine().Sync());

        Assert.Equal("ignored local path may be overwritten: private.cfg; move it before syncing", error.Message);
        Assert.Equal("private local data\n", File.ReadAllText(LocalFile("private.cfg")));
        Assert.Equal(before, Head());
    }

    [Fact]
    public void SyncRefusesAMissingDirectoryAndANonRepository()
    {
        var missing = Path.Combine(Root, "missing");
        var plain = Path.Combine(Root, "plain");
        Directory.CreateDirectory(plain);

        Assert.Equal($"{Path.GetFullPath(missing)} does not exist", Assert.Throws<RefusedException>(() => Engine(missing).Sync()).Message);
        Assert.Equal($"{Path.GetFullPath(plain)} is not a git working tree", Assert.Throws<RefusedException>(() => Engine(plain).Sync()).Message);
    }
}
