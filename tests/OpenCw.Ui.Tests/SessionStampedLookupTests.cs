using OpenCommonwealth.Session;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class SessionStampedLookupTests
{
    [Fact]
    public void EnterSessionClearsEntriesWhenTheSessionIdChanges()
    {
        var cache = new SessionStampedLookup<string, int>();
        var session = cache.EnterSession(1);
        cache.Store("a", 7, session, 1, true);
        Assert.True(cache.TryGet("a", out var value) && value == 7);
        cache.EnterSession(2);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void EnterSessionKeepsEntriesWhileTheSessionIdStaysTheSame()
    {
        var cache = new SessionStampedLookup<string, int>();
        var session = cache.EnterSession(3);
        cache.Store("a", 4, session, 3, true);
        cache.EnterSession(3);
        Assert.True(cache.TryGet("a", out var value) && value == 4);
    }

    [Fact]
    public void StorePersistsANullValueWhenTheReadFailedButWasCacheable()
    {
        var cache = new SessionStampedLookup<string, string?>();
        var session = cache.EnterSession(1);
        cache.Store("sndr", null, session, 1, true);
        Assert.True(cache.TryGet("sndr", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void StoreSkipsWhenTheReadWasNotCacheable()
    {
        var cache = new SessionStampedLookup<string, int?>();
        var session = cache.EnterSession(1);
        cache.Store("sndr", null, session, 1, false);
        Assert.False(cache.TryGet("sndr", out _));
    }

    [Fact]
    public void StoreSkipsWhenTheSessionChangedDuringTheRead()
    {
        var cache = new SessionStampedLookup<string, int>();
        cache.EnterSession(2);
        cache.Store("a", 9, 1, 2, true);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void LookupRetriesAfterANonCacheableReadButNotAfterACacheableFailure()
    {
        var cache = new SessionStampedLookup<string, int?>();
        var reads = 0;
        int? Resolve(bool persist)
        {
            var session = cache.EnterSession(10);
            if (persist && cache.TryGet("x", out var known)) return known;
            reads++;
            cache.Store("x", null, session, 10, persist);
            return null;
        }
        Resolve(true);
        Resolve(true);
        Assert.Equal(1, reads);
        Resolve(false);
        Assert.Equal(2, reads);
    }
}