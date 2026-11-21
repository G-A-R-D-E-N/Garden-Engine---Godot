using OpenCommonwealth.Services.Papyrus.Events;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class RetryingCacheTests
{
    [Fact]
    public void ACompleteResultIsReadOnce()
    {
        var cache = new RetryingCache<string>(1000);
        var reads = 0;
        Assert.Equal("a", cache.Get("k", 0, () => { reads++; return ("a", true); }));
        Assert.Equal("a", cache.Get("K", 1_000_000, () => { reads++; return ("b", true); }));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void AnIncompleteResultIsReadAgainOnlyAfterTheRetryDelay()
    {
        var cache = new RetryingCache<string>(1000);
        var reads = 0;
        Assert.Equal("missing", cache.Get("k", 0, () => { reads++; return ("missing", false); }));
        Assert.Equal("missing", cache.Get("k", 999, () => { reads++; return ("x", true); }));
        Assert.Equal(1, reads);
        Assert.Equal("found", cache.Get("k", 1000, () => { reads++; return ("found", true); }));
        Assert.Equal("found", cache.Get("k", 5000, () => { reads++; return ("later", true); }));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void ClearingForgetsEveryResult()
    {
        var cache = new RetryingCache<int>(1000);
        cache.Get("k", 0, () => (1, true));
        cache.Clear();
        Assert.Equal(2, cache.Get("k", 0, () => (2, true)));
    }
}
