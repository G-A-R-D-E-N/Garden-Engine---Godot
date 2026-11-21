using OpenCommonwealth.Services.Dialogue;
using OpenCommonwealth.Services.Ui.Dialogs;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VoicedNodeTests
{
    static readonly DialogueChoice[] Choices = [new DialogueChoice("Continue", Slot: 0)];
    static readonly string LongLine = new('x', 200);

    static DialogueNode Spoken(string line) => new("n", "Preston", line, Choices, 0, true, true);

    [Fact]
    public void A_voiced_line_with_subtitles_stays_up_as_long_as_the_same_text_unvoiced()
    {
        var voiced = VoiceTiming.Voiced(Spoken(LongLine), 1.0, subtitles: true);

        Assert.Equal(DialogueNode.ReadableMs(LongLine), voiced.LineDurationMs);
        Assert.Equal(Spoken(LongLine).SpeakingMs, voiced.SpeakingMs);
    }

    [Fact]
    public void A_clip_longer_than_the_reading_time_keeps_its_own_length_with_the_tail()
    {
        var voiced = VoiceTiming.Voiced(Spoken("Hi."), 4.0, subtitles: true);

        Assert.Equal(4000 + VoiceTiming.TailMs, voiced.LineDurationMs);
    }

    [Fact]
    public void A_short_clip_for_a_short_line_is_not_stretched_when_the_text_is_hidden()
    {
        var voiced = VoiceTiming.Voiced(Spoken(LongLine), 1.0, subtitles: false);

        Assert.Equal(1000 + VoiceTiming.TailMs, voiced.LineDurationMs);
    }

    [Fact]
    public void A_voiced_line_is_no_longer_forced_and_keeps_its_voice_reference()
    {
        var node = Spoken("Hi.") with { Voice = new VoiceRef(new Radicle.Interop.FormKey("Fallout4.esm", 0x10), 1, null) };

        var voiced = VoiceTiming.Voiced(node, 1.0, subtitles: true);

        Assert.False(voiced.ForceLine);
        Assert.Equal(node.Voice, voiced.Voice);
    }

    [Fact]
    public void A_clip_with_no_usable_length_leaves_the_node_forced_and_unchanged()
    {
        var node = Spoken("Hi.");

        Assert.Same(node, VoiceTiming.Voiced(node, 0, subtitles: true));
        Assert.Same(node, VoiceTiming.Voiced(node, double.NaN, subtitles: false));
    }

    [Fact]
    public void With_subtitles_off_only_an_unvoiced_line_is_still_shown()
    {
        var unvoiced = new DialogueSession(Spoken("Hi."), subtitles: false);
        var voiced = new DialogueSession(VoiceTiming.Voiced(Spoken("Hi."), 1.0, subtitles: false), subtitles: false);

        Assert.True(unvoiced.LineVisible);
        Assert.False(voiced.LineVisible);
    }

    [Fact]
    public void With_subtitles_on_a_voiced_line_is_shown_until_its_reading_time_has_passed()
    {
        var session = new DialogueSession(VoiceTiming.Voiced(Spoken(LongLine), 1.0, subtitles: true), subtitles: true);

        session.Tick(2.0);
        Assert.Equal(DialogueState.Speaking, session.State);
        Assert.True(session.LineVisible);

        session.Tick(DialogueNode.ReadableMs(LongLine) / 1000.0);
        Assert.Equal(DialogueState.Choosing, session.State);
    }
}
