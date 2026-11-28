using OpenCommonwealth.Services.Audio;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MusicTracksTests
{
    [Fact]
    public void UnplayableTracksAreSkippedAndEachTrackIsTriedAtMostOnce()
    {
        var tried = new List<int>();
        int result = MusicTracks.PlayFirstAvailable(3, 2, i => { tried.Add(i); return i == 1; });
        Assert.Equal(1, result);
        Assert.Equal(new[] { 2, 0, 1 }, tried);
        tried.Clear();
        Assert.Equal(-1, MusicTracks.PlayFirstAvailable(3, 1, i => { tried.Add(i); return false; }));
        Assert.Equal(new[] { 1, 2, 0 }, tried);
        Assert.Equal(-1, MusicTracks.PlayFirstAvailable(0, 0, _ => throw new Exception()));
        tried.Clear();
        Assert.Equal(0, MusicTracks.PlayFirstAvailable(3, -1, i => { tried.Add(i); return i == 0; }));
        Assert.Equal(new[] { 2, 0 }, tried);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(4, 2)]
    public void AdvancePicksADifferentIndexWhenMoreThanOneTrackExists(int count, int current)
    {
        for (int i = 0; i < 32; i++)
        {
            int next = MusicTracks.Advance(count, current);
            Assert.InRange(next, 0, Math.Max(0, count - 1));
            if (count > 1) Assert.NotEqual(current, next);
            else Assert.Equal(0, next);
        }
    }
}
