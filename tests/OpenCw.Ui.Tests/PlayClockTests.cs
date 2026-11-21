using System;
using System.Collections.Generic;
using System.Text;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PlayClockTests
{
    [Fact]
    public void ElapsedCountsFromTheStartOnTopOfWhatWasCarried()
    {
        var clock = new PlayClock();
        clock.Start(100, 30);

        Assert.Equal(TimeSpan.FromSeconds(75), clock.Elapsed(145));
        Assert.Equal(TimeSpan.FromSeconds(30), clock.Elapsed(50));
    }

    [Fact]
    public void ASavedSectionRestoresThePlaytimeOnTheNextStart()
    {
        var clock = new PlayClock();
        clock.Start(0, 0);
        var sections = clock.Sections(90);

        var loaded = new PlayClock();
        loaded.Start(500, PlayClock.Read(sections)!.Value);

        Assert.Equal(TimeSpan.FromSeconds(100), loaded.Elapsed(510));
    }

    [Fact]
    public void ASaveWithoutTheSectionReadsAsNull() => Assert.Null(PlayClock.Read(new Dictionary<string, byte[]>()));

    [Theory]
    [InlineData("{\"version\":1,\"seconds\":-1}")]
    [InlineData("{\"version\":1,\"seconds\":2e9}")]
    [InlineData("{\"version\":1,\"seconds\":\"x\"}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":3,\"seconds\":1}")]
    [InlineData("not json")]
    public void AMalformedSectionIsRefused(string json)
    {
        var sections = new Dictionary<string, byte[]> { [PlayClock.SectionName] = Encoding.UTF8.GetBytes(json) };

        Assert.Throws<FormatException>(() => PlayClock.Read(sections));
    }
}
