using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SizedCacheTests
{
    [Fact]
    public void The_least_recently_used_values_go_when_the_budget_is_passed_but_the_newest_stays()
    {
        var cache = new SizedCache<string>(100, () => 0, 10);
        cache.Add("a", "A", 60);
        cache.Add("b", "B", 30);
        Assert.True(cache.TryGet("a", out _));
        cache.Add("c", "C", 30);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out var a));
        Assert.Equal("A", a);
        Assert.Equal(90, cache.Bytes);
        cache.Add("huge", "H", 500);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("huge", out _));
    }

    [Fact]
    public void A_failed_key_is_read_again_after_the_retry_delay_and_a_success_replaces_it()
    {
        long now = 0;
        var cache = new SizedCache<string>(100, () => now, 30);
        cache.Fail("x");
        Assert.True(cache.TryGet("x", out var failed));
        Assert.Null(failed);
        now = 29;
        Assert.True(cache.TryGet("x", out _));
        now = 30;
        Assert.False(cache.TryGet("x", out _));
        cache.Fail("x");
        cache.Add("x", "X", 5);
        Assert.True(cache.TryGet("x", out var ok));
        Assert.Equal("X", ok);
        Assert.Equal(5, cache.Bytes);
    }

    [Fact]
    public void The_attempt_gate_allows_a_bounded_number_of_tries_until_success_or_reset()
    {
        var gate = new AttemptGate(2);
        Assert.True(gate.TryBegin());
        Assert.True(gate.TryBegin());
        Assert.False(gate.TryBegin());
        gate.Reset();
        Assert.True(gate.TryBegin());
        gate.Succeeded();
        Assert.False(gate.TryBegin());
    }
}
