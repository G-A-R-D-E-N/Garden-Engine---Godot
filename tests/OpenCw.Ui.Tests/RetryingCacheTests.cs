using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RetryingCacheTests
{
    [Fact]
    public void A_complete_read_is_kept_and_an_incomplete_one_is_read_again()
    {
        var calls = 0;
        var cache = new RetryingCache<int, int?>(_ => ++calls == 1 ? null : 7, value => value is not null);
        Assert.Null(cache.Get(1));
        Assert.Equal(7, cache.Get(1));
        Assert.Equal(7, cache.Get(1));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void A_permanent_gap_stops_being_read_after_the_attempt_limit()
    {
        var calls = 0;
        var cache = new RetryingCache<int, int?>(_ => { calls++; return null; }, value => value is not null, attempts: 3);
        for (var i = 0; i < 10; i++) Assert.Null(cache.Get(1));
        Assert.Equal(3, calls);
        cache.Clear();
        cache.Get(1);
        Assert.Equal(4, calls);
    }
}
