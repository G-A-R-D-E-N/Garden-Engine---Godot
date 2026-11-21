using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class HudSubtitleTests
{
    private static double Measure(string text) => text.Length * 10;

    private static SubtitleQueue Queue(double width = 100, int maxLines = 2, int maxActive = 2) =>
        new(Measure, width, maxLines, maxActive);

    [Fact]
    public void WrapBreaksOnWordsWhenTheWidthIsExceeded()
    {
        Assert.Equal(new[] { "hello", "world foo" }, TextWrap.Wrap("hello world foo", 100, Measure));
    }

    [Fact]
    public void WrapSplitsAWordThatCannotFit()
    {
        Assert.Equal(new[] { "abcdefghij", "klmno" }, TextWrap.Wrap("abcdefghijklmno", 100, Measure));
        Assert.Equal(new[] { "hi", "abcdefghij", "klmno" }, TextWrap.Wrap("hi abcdefghijklmno", 100, Measure));
    }

    [Fact]
    public void WrapNeverSplitsASurrogatePair()
    {
        string word = string.Concat(Enumerable.Repeat("😀", 8));
        var lines = TextWrap.Wrap(word, 90, text => text.Length * 10);
        Assert.Equal(new[] { string.Concat(Enumerable.Repeat("😀", 5)), string.Concat(Enumerable.Repeat("😀", 3)) }, lines);
        Assert.All(lines, line =>
        {
            Assert.False(char.IsLowSurrogate(line[0]), "starts with a lone low surrogate");
            Assert.False(char.IsHighSurrogate(line[^1]), "ends with a lone high surrogate");
        });
        Assert.Equal(word, string.Concat(lines));
    }

    [Fact]
    public void WrapKeepsParagraphsAndIgnoresBlankInput()
    {
        Assert.Equal(new[] { "a", "b" }, TextWrap.Wrap("a\r\n\nb", 100, Measure));
        Assert.Empty(TextWrap.Wrap("   ", 100, Measure));
        Assert.Empty(TextWrap.Wrap("", 100, Measure));
    }

    [Fact]
    public void WrapWithoutAWidthLeavesLinesAlone()
    {
        Assert.Equal(new[] { "one two three" }, TextWrap.Wrap("one two three", 0, Measure));
    }

    [Fact]
    public void WrapNeverProducesALineWiderThanTheLimit()
    {
        var lines = TextWrap.Wrap("the quick brown fox jumps over the lazy dog again and again", 70, Measure);
        Assert.All(lines, line => Assert.True(Measure(line) <= 70, line));
        Assert.Equal("thequickbrownfoxjumpsoverthelazydogagainandagain", string.Concat(lines.Select(l => l.Replace(" ", ""))));
    }

    [Fact]
    public void ALineShowsWithItsSpeakerAndExpiresOnTime()
    {
        var queue = Queue();
        queue.Add("hello world foo", "Ann", seconds: 2);
        var view = Assert.Single(queue.Visible);
        Assert.Equal("Ann", view.Speaker);
        Assert.Equal(new[] { "hello", "world foo" }, view.Lines);

        queue.Tick(1.5);
        Assert.Single(queue.Visible);
        queue.Tick(0.5);
        Assert.Empty(queue.Visible);
    }

    [Fact]
    public void DurationDefaultsToReadingSpeedWithAFloor()
    {
        var queue = Queue(width: 1000);
        queue.CharsPerSecond = 10;
        queue.MinSeconds = 1;
        queue.Add(new string('x', 50));
        queue.Tick(4.5);
        Assert.Single(queue.Visible);
        queue.Tick(0.5);
        Assert.Empty(queue.Visible);

        queue.Add("hi");
        queue.Tick(0.75);
        Assert.Single(queue.Visible);
        queue.Tick(0.25);
        Assert.Empty(queue.Visible);
    }

    [Fact]
    public void LongTextIsPagedByTheLineLimitWithTheTimeSplit()
    {
        var queue = Queue(width: 50, maxLines: 1);
        queue.Add("aaaa bbbb cccc dddd", "Ann", seconds: 8);
        Assert.Equal(3, queue.PendingCount);
        Assert.Equal("aaaa", Assert.Single(queue.Visible).Lines[0]);

        queue.Tick(2);
        Assert.Equal("bbbb", Assert.Single(queue.Visible).Lines[0]);
        queue.Tick(2);
        Assert.Equal("cccc", Assert.Single(queue.Visible).Lines[0]);
        queue.Tick(2);
        Assert.Equal("dddd", Assert.Single(queue.Visible).Lines[0]);
        queue.Tick(2);
        Assert.Empty(queue.Visible);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void DifferentSpeakersShowTogetherUpToTheActiveLimit()
    {
        var queue = Queue(maxActive: 2);
        queue.Add("hi", "A", seconds: 2);
        queue.Add("yo", "B", seconds: 2);
        queue.Add("hey", "C", seconds: 2);
        Assert.Equal(new[] { "A", "B" }, queue.Visible.Select(v => v.Speaker).ToArray());
        Assert.Equal(1, queue.PendingCount);

        queue.Tick(2);
        Assert.Equal("C", Assert.Single(queue.Visible).Speaker);
    }

    [Fact]
    public void ASpeakerNeverTalksOverThemselves()
    {
        var queue = Queue();
        queue.Add("first", "A", seconds: 2);
        queue.Add("second", "A", seconds: 2);
        Assert.Equal("first", Assert.Single(queue.Visible).Lines[0]);
        Assert.Equal(1, queue.PendingCount);

        queue.Tick(2);
        Assert.Equal("second", Assert.Single(queue.Visible).Lines[0]);
    }

    [Fact]
    public void ALaterSpeakerIsNotBlockedBySkippedSameSpeakerLines()
    {
        var queue = Queue();
        queue.Add("a1", "A", seconds: 2);
        queue.Add("a2", "A", seconds: 2);
        queue.Add("b1", "B", seconds: 2);
        Assert.Equal(new[] { "A", "B" }, queue.Visible.Select(v => v.Speaker).ToArray());
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void FadeEnvelopeRampsInAndOut()
    {
        var queue = Queue();
        queue.Add("hello", "A", seconds: 2);
        Assert.Equal(0.0, queue.Visible[0].Alpha, 6);

        queue.Tick(1);
        Assert.Equal(1.0, queue.Visible[0].Alpha, 6);

        queue.Tick(0.9);
        Assert.Equal(0.1 / 0.15, queue.Visible[0].Alpha, 3);
    }

    [Fact]
    public void DisablingClearsAndBlocksNewLines()
    {
        var queue = Queue();
        queue.Add("hello", "A", seconds: 2);
        queue.Enabled = false;
        Assert.Empty(queue.Visible);

        queue.Add("again", "A", seconds: 2);
        Assert.Empty(queue.Visible);

        queue.Enabled = true;
        queue.Add("back", "A", seconds: 2);
        Assert.Single(queue.Visible);
    }

    [Fact]
    public void WidthChangesApplyToLaterLines()
    {
        var queue = Queue(width: 1000);
        queue.Add("hello world", "A", seconds: 2);
        Assert.Single(queue.Visible[0].Lines);

        queue.MaxWidth = 60;
        queue.Add("hello world", "B", seconds: 2);
        Assert.Equal(2, queue.Visible[1].Lines.Count);
    }

    [Fact]
    public void BlankTextAndBadArgumentsAreHandled()
    {
        var queue = Queue();
        queue.Add("   ", "A");
        Assert.Empty(queue.Visible);
        Assert.Throws<System.ArgumentNullException>(() => new SubtitleQueue(null!, 100));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new SubtitleQueue(Measure, 100, maxLines: 0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new SubtitleQueue(Measure, 100, maxActive: 0));
    }

    [Fact]
    public void PendingLinesAreCappedAndTheOldestAreDropped()
    {
        var queue = Queue(width: 1000);
        for (var i = 0; i < 100; i++) queue.Add($"line{i}", "A", seconds: 2);

        Assert.Equal(SubtitleQueue.MaxPending, queue.PendingCount);
        Assert.Equal("line0", Assert.Single(queue.Visible).Lines[0]);

        queue.Tick(2);
        Assert.Equal($"line{100 - SubtitleQueue.MaxPending}", Assert.Single(queue.Visible).Lines[0]);
        Assert.Equal(SubtitleQueue.MaxPending - 1, queue.PendingCount);

        for (var i = 0; i < SubtitleQueue.MaxPending - 1; i++) queue.Tick(2);
        Assert.Equal("line99", Assert.Single(queue.Visible).Lines[0]);
        Assert.Equal(0, queue.PendingCount);
    }
}
