using System.Text;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Save;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class AnimationTracksSaveTests
{
    private sealed class Clips : IAnimationClips
    {
        public AnimationClip? Find(string refKey, string animation) => new(animation, 2f, []);
    }

    [Fact]
    public void RunningTracksRoundTripThroughTheSection()
    {
        var live = new ReferenceAnimations(new Clips());
        live.Play("000B00", "Open");
        live.Play("000A00", "Close");
        live.Advance(0.5);

        var sections = AnimationTracksSave.Sections(live);
        var rows = AnimationTracksSave.Read(sections);

        Assert.Equal(new[] { new TrackRow("000A00", "Close", 0.5), new TrackRow("000B00", "Open", 0.5) }, rows!.ToArray());
    }

    [Fact]
    public void NoRunningTrackWritesNoSectionAndAnOldSaveReadsAsNull()
    {
        var sections = AnimationTracksSave.Sections(new ReferenceAnimations(new Clips()));

        Assert.Empty(sections);
        Assert.Null(AnimationTracksSave.Read(sections));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"tracks\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"tracks\":[{\"ref\":\"\",\"animation\":\"Open\",\"time\":1}]}")]
    [InlineData("{\"version\":1,\"tracks\":[{\"ref\":\"A\",\"animation\":\"Open\",\"time\":-1}]}")]
    [InlineData("{\"version\":1,\"tracks\":[{\"ref\":\"A\",\"animation\":\"Open\"}]}")]
    [InlineData("{\"version\":1,\"tracks\":[{\"ref\":1,\"animation\":\"Open\",\"time\":1}]}")]
    public void AMalformedSectionIsRefusedAsAFormatError(string json)
    {
        var sections = new Dictionary<string, byte[]> { [AnimationTracksSave.SectionName] = Encoding.UTF8.GetBytes(json) };

        Assert.Throws<FormatException>(() => AnimationTracksSave.Read(sections));
    }

    [Fact]
    public void MoreRowsThanTheTableHoldsIsRefused()
    {
        var rows = Enumerable.Range(0, ReferenceAnimations.Capacity + 1).Select(index => new TrackRow("R" + index, "Open", 0.1)).ToList();
        var sections = new Dictionary<string, byte[]> { [AnimationTracksSave.SectionName] = Encoding.UTF8.GetBytes(AnimationTracksSave.Serialize(rows)) };

        Assert.Throws<FormatException>(() => AnimationTracksSave.Read(sections));
    }
}
