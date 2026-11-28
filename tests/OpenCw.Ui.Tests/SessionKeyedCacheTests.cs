using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class SessionKeyedCacheTests
{
    [Fact]
    public void SameSessionReturnsTheCachedValue()
    {
        var cache = new SessionKeyedCache<int, string?>();
        cache.Set(1, 7, "seven");
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(1, 7, out var value));
        Assert.Equal("seven", value);
    }

    [Fact]
    public void SyncSessionReportsAChangeOnlyWhenTheSessionDiffers()
    {
        var cache = new SessionKeyedCache<int, string?>();
        Assert.True(cache.SyncSession(1));
        Assert.False(cache.SyncSession(1));
        Assert.True(cache.SyncSession(2));
        Assert.False(cache.SyncSession(2));
    }

    [Fact]
    public void SessionChangeInvalidatesEveryEntry()
    {
        var cache = new SessionKeyedCache<int, string?>();
        cache.Set(1, 7, "seven");
        cache.Set(1, 8, "eight");
        Assert.Equal(2, cache.Count);
        Assert.True(cache.SyncSession(2));
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(2, 7, out _));
        Assert.False(cache.TryGet(2, 8, out _));
    }

    [Fact]
    public void ClearRemovesEntriesAndKeepsTheSession()
    {
        var cache = new SessionKeyedCache<int, string?>();
        cache.Set(1, 7, "seven");
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(1, 7, out _));
        Assert.False(cache.SyncSession(1));
        cache.Set(1, 7, "again");
        Assert.True(cache.TryGet(1, 7, out var value));
        Assert.Equal("again", value);
    }

    [Fact]
    public void MissesAreNotCached()
    {
        var cache = new SessionKeyedCache<int, string?>();
        Assert.False(cache.TryGet(1, 5, out var missing));
        Assert.Null(missing);
        Assert.Equal(0, cache.Count);
        cache.Set(1, 5, "five");
        Assert.True(cache.TryGet(1, 5, out var value));
        Assert.Equal("five", value);
        Assert.False(cache.TryGet(2, 5, out _));
        Assert.False(cache.TryGet(1, 5, out _));
    }

    [Fact]
    public void ConstructorComparerIsHonoured()
    {
        var cache = new SessionKeyedCache<string, int>(StringComparer.OrdinalIgnoreCase);
        cache.Set(1, "Voice", 3);
        Assert.True(cache.TryGet(1, "voice", out var value));
        Assert.Equal(3, value);
    }

    [Fact]
    public void GetOrResolveDoesNotCacheWhenResolverMarksResultUncacheable()
    {
        var cache = new SessionKeyedCache<int, string?>();
        int calls = 0;
        var first = cache.GetOrResolve(1, 42, () => 1, () => { calls++; return (false, null); });
        Assert.Null(first);
        Assert.Equal(0, cache.Count);
        var second = cache.GetOrResolve(1, 42, () => 1, () => { calls++; return (true, "MaleEvenToned"); });
        Assert.Equal("MaleEvenToned", second);
        Assert.Equal(2, calls);
        Assert.Equal(1, cache.Count);
        var third = cache.GetOrResolve(1, 42, () => 1, () => { calls++; return (true, "Other"); });
        Assert.Equal("MaleEvenToned", third);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void GetOrResolveSkipsCachingWhenSessionAdvancesDuringResolve()
    {
        var cache = new SessionKeyedCache<int, string?>();
        int liveSession = 1;
        var value = cache.GetOrResolve(1, 7, () => liveSession, () =>
        {
            liveSession = 2;
            return (true, "StaleVoice");
        });
        Assert.Equal("StaleVoice", value);
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(2, 7, out _));
    }

    [Fact]
    public void GetOrResolveReResolvesAfterSessionChange()
    {
        var cache = new SessionKeyedCache<int, string?>();
        int liveSession = 1;
        int calls = 0;
        string? Resolve(string name) => cache.GetOrResolve(liveSession, 7, () => liveSession, () => { calls++; return (true, name); });
        Assert.Equal("Old", Resolve("Old"));
        Assert.Equal("Old", Resolve("Ignored"));
        Assert.Equal(1, calls);
        liveSession = 2;
        Assert.Equal("New", Resolve("New"));
        Assert.Equal(2, calls);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void GetOrResolveWhileUninitializedStaysUncachedUntilRecordsLoad()
    {
        var cache = new SessionKeyedCache<int, string?>();
        bool loaded = false;
        string? Resolve() => cache.GetOrResolve(0, 7, () => 0, () => loaded ? (true, "Voice") : (false, null));
        Assert.Null(Resolve());
        Assert.Null(Resolve());
        Assert.Equal(0, cache.Count);
        loaded = true;
        Assert.Equal("Voice", Resolve());
        Assert.Equal(1, cache.Count);
    }
}
