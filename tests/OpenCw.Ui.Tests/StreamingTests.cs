using System.Threading.Tasks;
using OpenCommonwealth.Services.Ui.Streaming;
using Xunit;

namespace OpenCw.Ui.Tests;

public class StreamingTests
{
    [Fact]
    public void ClaimByTokenSucceedsOnlyForTheOwningDrain()
    {
        var reg = new DrainRegistry();
        var first = reg.Begin(new[] { "a" })["a"];
        var second = reg.Begin(new[] { "a" })["a"];
        Assert.False(reg.Claim("a", first));
        Assert.True(reg.Owns("a", second));
        Assert.True(reg.Claim("a", second));
        Assert.False(reg.Claim("a", second));
    }

    [Fact]
    public void RerequestClaimsTheDrainBackAndStalesItsToken()
    {
        var reg = new DrainRegistry();
        var token = reg.Begin(new[] { "a", "b" })["a"];
        Assert.True(reg.Claim("a"));
        Assert.False(reg.Claim("a"));
        Assert.False(reg.Claim("a", token));
        Assert.False(reg.RunIfOwner("a", token, () => Assert.Fail("ran for a stale token")));
    }

    [Fact]
    public void RunIfOwnerRunsOnlyWhileTheTokenOwnsThePath()
    {
        var reg = new DrainRegistry();
        var token = reg.Begin(new[] { "a" })["a"];
        int runs = 0;
        Assert.True(reg.RunIfOwner("a", token, () => runs++));
        Assert.Equal(1, runs);
        Assert.False(reg.RunIfOwner("a", new object(), () => runs++));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void BackoffHoldsUntilTheRetryFrameThenReleases()
    {
        var b = new RetryBackoff<string>();
        Assert.False(b.BackedOff("p", 10));
        b.Fail("p", 10, 30);
        Assert.True(b.BackedOff("p", 39));
        Assert.False(b.BackedOff("p", 40));
    }

    [Fact]
    public void BackoffClearsOnSuccessAndWhenTheKeyLeavesTheSelection()
    {
        var b = new RetryBackoff<string>();
        b.Fail("a", 0, 100);
        b.Fail("b", 0, 100);
        b.Fail("c", 0, 100);
        b.Clear("a");
        b.Prune(new[] { "b" });
        Assert.False(b.BackedOff("a", 1));
        Assert.True(b.BackedOff("b", 1));
        Assert.False(b.BackedOff("c", 1));
    }

    [Fact]
    public async Task ThenRunsOnlyAfterEveryTaskFinishesEvenWhenOneFails()
    {
        var slow = new TaskCompletionSource();
        var failed = Task.FromException(new System.InvalidOperationException());
        bool closed = false;
        var done = AfterAll.Run(new[] { slow.Task, failed }, () => closed = true);
        await Task.Delay(50);
        Assert.False(closed);
        slow.SetResult();
        await done;
        Assert.True(closed);
    }
}
