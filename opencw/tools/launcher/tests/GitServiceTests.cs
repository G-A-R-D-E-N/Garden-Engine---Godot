using LibGit2Sharp;
using Xunit;

namespace OpenCommonwealth.Launcher.Tests;

[Collection("GitServiceStatics")]
public class CredentialTests
{
    [Fact]
    public void GhTokenIsUsedWhenGitCredentialLookupFails()
    {
        var r = GitService.CredentialFrom(() => null, () => "tok");
        Assert.NotNull(r);
        Assert.Equal("x-access-token", r!.Value.user);
        Assert.Equal("tok", r.Value.secret);
    }

    [Fact]
    public void GitCredentialWinsWhenPresent()
    {
        var r = GitService.CredentialFrom(() => ("me", "pw"), () => "tok");
        Assert.NotNull(r);
        Assert.Equal("me", r!.Value.user);
        Assert.Equal("pw", r.Value.secret);
    }

    [Fact]
    public void RefreshedTokenIsUsedForGitInsteadOfTheStaleStoredOne()
    {
        var r = GitService.Verified("fresh");
        Assert.NotNull(r);
        Assert.Equal("x-access-token", r!.Value.user);
        Assert.Equal("fresh", r.Value.secret);
    }

    [Fact]
    public void NoVerifiedTokenMeansStoredCredentialsAreUsed()
    {
        Assert.Null(GitService.Verified(null));
        Assert.Null(GitService.Verified(""));
    }

    [Fact]
    public void NoCredentialWhenBothSourcesAreEmpty()
    {
        Assert.Null(GitService.CredentialFrom(() => null, () => null));
    }
}

[Collection("GitServiceStatics")]
public class GitServiceTests
{
    static readonly Signature Sig = new("t", "t@example.com", DateTimeOffset.Now);

    static (string origin, string work) MakeRepos(string root)
    {
        string origin = Path.Combine(root, "origin");
        string work = Path.Combine(root, "work");
        Repository.Init(origin);
        using (var o = new Repository(origin))
        {
            File.WriteAllText(Path.Combine(origin, "a.txt"), "1");
            Commands.Stage(o, "a.txt");
            o.Commit("one", Sig, Sig);
            if (o.Head.FriendlyName != "main") o.Branches.Rename(o.Head, "main");
        }
        Repository.Clone(origin, work, new CloneOptions { BranchName = "main" });
        using (var o = new Repository(origin))
        {
            File.WriteAllText(Path.Combine(origin, "b.txt"), "2");
            Commands.Stage(o, "b.txt");
            o.Commit("two", Sig, Sig);
        }
        return (origin, work);
    }

    static void PlantUserMetadata(string work)
    {
        using var r = new Repository(work);
        r.Refs.Add("refs/fetched/mine", r.Head.Tip.Id);
        r.Network.Remotes.Add("_ocw_srccheck", "https://example.com/keep.git");
    }

    static void AssertUserMetadataIntact(string work)
    {
        using var r = new Repository(work);
        Assert.NotNull(r.Refs["refs/fetched/mine"]);
        Assert.Equal("https://example.com/keep.git", r.Network.Remotes["_ocw_srccheck"]?.Url);
        Assert.DoesNotContain(r.Refs, x => x.CanonicalName.StartsWith("refs/garden-launcher/", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Network.Remotes, x => x.Name.StartsWith("_gl_srccheck_", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceCheckLeavesExistingRefsAndRemotesAlone()
    {
        using var t = new TempDir();
        var (origin, work) = MakeRepos(t.Path);
        PlantUserMetadata(work);

        var s = new GitService(work, origin).CheckSource();

        Assert.Equal(GitService.Reach.Ok, s.Reach);
        Assert.Equal(1, s.Behind);
        AssertUserMetadataIntact(work);
    }

    [Fact]
    public void PullLeavesExistingRefsAndRemotesAlone()
    {
        using var t = new TempDir();
        var (origin, work) = MakeRepos(t.Path);
        PlantUserMetadata(work);

        string? err = new GitService(work, origin).Pull(out int pulled, out _, out _);

        Assert.Null(err);
        Assert.Equal(1, pulled);
        Assert.True(File.Exists(Path.Combine(work, "b.txt")));
        AssertUserMetadataIntact(work);
    }

    [Fact]
    public void PullKeepsUncommittedWork()
    {
        using var t = new TempDir();
        var (origin, work) = MakeRepos(t.Path);
        File.WriteAllText(Path.Combine(work, "a.txt"), "edited");
        File.WriteAllText(Path.Combine(work, "untracked.txt"), "mine");

        string? err = new GitService(work, origin).Pull(out _, out string? note, out bool attention);

        Assert.Null(err);
        Assert.False(attention, note);
        Assert.Equal("edited", File.ReadAllText(Path.Combine(work, "a.txt")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(work, "untracked.txt")));
        Assert.True(File.Exists(Path.Combine(work, "b.txt")));
    }
}
