using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class LookupCacheTests
{
    private static LookupCache<string> Cache(List<string>? reported = null) =>
        new(ex => ex is InvalidOperationException, (key, ex) => reported?.Add($"{key}: {ex.Message}"));

    [Fact]
    public void AFailedReadIsNotStoredAndTheNextCallRetriesAndCachesTheAnswer()
    {
        var reported = new List<string>();
        var cache = Cache(reported);
        var calls = 0;

        Assert.Null(cache.Get("a", () => { calls++; throw new InvalidOperationException("boom"); }));
        Assert.Equal("ok", cache.Get("a", () => { calls++; return "ok"; }));
        Assert.Equal("ok", cache.Get("a", () => { calls++; return "never"; }));

        Assert.Equal(2, calls);
        Assert.Equal(1, cache.Failures);
        Assert.Equal(["a: boom"], reported);
    }

    [Fact]
    public void AConfirmedMissingRecordIsCachedAsNull()
    {
        var cache = Cache();
        var calls = 0;

        Assert.Null(cache.Get("a", () => { calls++; return null; }));
        Assert.Null(cache.Get("a", () => { calls++; return "late"; }));

        Assert.Equal(1, calls);
        Assert.Equal(0, cache.Failures);
    }

    [Fact]
    public void AnExceptionThatIsNotTransientPropagatesAndClearForgetsAnswers()
    {
        var cache = Cache();
        Assert.Throws<ArgumentException>(() => cache.Get("a", () => throw new ArgumentException("bug")));
        Assert.Equal("x", cache.Get("b", () => "x"));
        cache.Clear();
        Assert.Equal("y", cache.Get("b", () => "y"));
    }
}
