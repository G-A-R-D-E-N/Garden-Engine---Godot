using System;
using OpenCommonwealth.Services.Dialogue;
using Radicle.Interop;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class LipTrackTests
{
    const int Frames = 31;

    static LipTrack Track(int preroll = 0, int phonemes = 2, int modifiers = 1, int frames = Frames)
    {
        int slots = phonemes + modifiers;
        var weights = new float[frames * slots];
        for (int f = 0; f < frames; f++)
        {
            weights[f * slots] = f % 2;
            if (slots > 1) weights[f * slots + 1] = f / 32f;
            if (slots > 2) weights[f * slots + 2] = 0.75f;
        }
        return new LipTrack(new LipAnimation(frames, preroll, phonemes, modifiers, weights));
    }

    [Theory]
    [InlineData(0, 0.0, 0.0)]
    [InlineData(0, 2.0, 60.0)]
    [InlineData(-5, 2.0, 65.0)]
    [InlineData(5, 2.0, 65.0)]
    [InlineData(-5, 0.0, 5.0)]
    [InlineData(0, -1.0, -30.0)]
    public void The_frame_position_is_thirty_frames_a_second_plus_the_absolute_preroll(int preroll, double seconds, double expected)
    {
        Assert.Equal(expected, Track(preroll).FramePosition(seconds));
    }

    [Fact]
    public void The_slot_count_is_phonemes_plus_modifiers()
    {
        Assert.Equal(3, Track().SlotCount);
        Assert.Equal(130, Track(phonemes: 129, modifiers: 1, frames: 1).SlotCount);
    }

    [Fact]
    public void A_sample_on_a_whole_frame_returns_that_frames_weights()
    {
        var slots = new float[3];

        Track().Sample(0.5, slots);

        Assert.Equal(new[] { 1f, 0.46875f, 0.75f }, slots);
    }

    [Fact]
    public void A_sample_between_two_frames_blends_them_linearly()
    {
        var slots = new float[3];

        Track().Sample(0.25, slots);

        Assert.Equal(new[] { 0.5f, 0.234375f, 0.75f }, slots);
    }

    [Fact]
    public void A_negative_preroll_shifts_the_sample_by_its_absolute_value()
    {
        var slots = new float[3];

        Track(preroll: -5).Sample(0.25, slots);

        Assert.Equal(new[] { 0.5f, 0.390625f, 0.75f }, slots);
    }

    [Fact]
    public void The_last_frame_is_a_valid_position_and_anything_past_it_is_silent()
    {
        var slots = new float[3];

        Track().Sample(1.0, slots);
        Assert.Equal(new[] { 0f, 0.9375f, 0.75f }, slots);

        Track(preroll: 1).Sample(1.0, slots);
        Assert.Equal(new[] { 0f, 0f, 0f }, slots);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_time_outside_the_track_or_not_a_number_clears_the_slots(double seconds)
    {
        var slots = new[] { 9f, 9f, 9f };

        Track().Sample(seconds, slots);

        Assert.Equal(new[] { 0f, 0f, 0f }, slots);
    }

    [Fact]
    public void A_track_with_no_frames_clears_the_slots()
    {
        var slots = new[] { 9f, 9f, 9f };

        Track(frames: 0).Sample(0.0, slots);

        Assert.Equal(new[] { 0f, 0f, 0f }, slots);
    }

    [Fact]
    public void A_slots_span_shorter_than_the_slot_count_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Track().Sample(0.0, new float[2]));
    }

    [Fact]
    public void A_longer_span_is_only_written_for_the_slot_count()
    {
        var slots = new[] { 9f, 9f, 9f, 9f, 9f };

        Track().Sample(0.5, slots);

        Assert.Equal(new[] { 1f, 0.46875f, 0.75f, 9f, 9f }, slots);
    }

    [Fact]
    public void The_jaw_opens_by_the_phoneme_slots_only_and_stays_shut_outside_the_track()
    {
        var track = Track();

        Assert.Equal(0.734375f, track.JawOpen(0.25));
        Assert.Equal(1f, track.JawOpen(0.5));
        Assert.Equal(0f, track.JawOpen(-1.0));
    }

    [Fact]
    public void A_track_with_more_than_128_slots_still_measures_the_jaw()
    {
        var weights = new float[130];
        weights[0] = 0.25f;
        weights[1] = 0.25f;
        weights[129] = 1f;
        var track = new LipTrack(new LipAnimation(1, 0, 129, 1, weights));

        Assert.Equal(0.5f, track.JawOpen(0.0));
    }

    [Fact]
    public void The_jaw_weight_halfway_down_the_neck_fade_is_one_half_and_a_quarter_in_is_the_smoothstep_of_it()
    {
        var mouth = new MouthBounds(10f, 2f);

        Assert.Equal(0.5f, JawMorph.Weight(10f - 7.5f, mouth));
        Assert.Equal(0.84375f, JawMorph.Weight(10f - 6.25f, mouth));
    }

    [Fact]
    public void Openness_ignores_phoneme_slots_beyond_a_shorter_span()
    {
        Assert.Equal(0.5f, MouthShape.Openness([0.5f], 3));
    }
}
