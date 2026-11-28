using OpenCommonwealth.Services;
using Xunit;

namespace OpenCw.Ui.Tests;

public class BoundedLruTests
{
    [Fact]
    public void EvictsTheLeastRecentlyUsedEntryAtCapacity()
    {
        var lru = new BoundedLru<string, int?>(2, StringComparer.OrdinalIgnoreCase);
        lru.Set("a", 1);
        lru.Set("b", 2);
        Assert.True(lru.TryGet("A", out _));
        lru.Set("c", 3);
        Assert.False(lru.TryGet("b", out _));
        Assert.True(lru.TryGet("a", out var a) && a == 1);
        Assert.True(lru.TryGet("c", out _));
        Assert.Equal(2, lru.Count);
    }

    [Fact]
    public void SessionChangeClearsEntriesUnderTheSameLock()
    {
        var lru = new BoundedLru<string, int>(2);
        lru.Set(1, "a", 1);
        Assert.True(lru.TryGet(1, "a", out var value) && value == 1);
        lru.Set(2, "b", 2);
        Assert.False(lru.TryGet(2, "a", out _));
        Assert.True(lru.TryGet(2, "b", out var b) && b == 2);
    }

    [Fact]
    public void KeepsNullValuesAndOverwritesWithoutGrowing()
    {
        var lru = new BoundedLru<string, string?>(2);
        lru.Set("a", null);
        lru.Set("a", "x");
        lru.Set("b", null);
        Assert.Equal(2, lru.Count);
        Assert.True(lru.TryGet("b", out var b) && b is null);
        lru.Clear();
        Assert.Equal(0, lru.Count);
        Assert.False(lru.TryGet("a", out _));
    }
}
