using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class RadioAudioPlanTests
{
    static readonly RadioStation[] Stations =
    {
        new("s1", "Diamond City Radio"),
        new("s2", "Off Air", Enabled: false),
        new("s3", "   "),
    };

    [Fact]
    public void Playing_an_enabled_station_starts_its_file()
    {
        // Detects: a chosen station that must start audio from its radio file rather than only change the menu.
        var step = RadioAudioPlan.Step(new RadioEvent(RadioEventKind.Play, "s1"), Stations);
        Assert.Equal(new RadioAudioStep(RadioAudioAction.Start, "Music/Radio/DiamondCityRadio.xwm"), step);
    }

    [Theory]
    [InlineData("s2")]
    [InlineData("s3")]
    [InlineData("missing")]
    [InlineData("tape:t1")]
    public void Stations_without_a_signal_or_file_and_tapes_play_nothing(string id)
    {
        // Detects: a disabled, unnamed, unknown or tape row that must not start station audio.
        Assert.Equal(RadioAudioAction.None, RadioAudioPlan.Step(new RadioEvent(RadioEventKind.Play, id), Stations).Action);
    }

    [Fact]
    public void Stop_and_mute_commands_map_straight_through()
    {
        // Detects: stop, mute and unmute commands that must reach the audio player unchanged.
        Assert.Equal(RadioAudioAction.Stop, RadioAudioPlan.Step(new RadioEvent(RadioEventKind.Stop), Stations).Action);
        Assert.Equal(RadioAudioAction.Mute, RadioAudioPlan.Step(new RadioEvent(RadioEventKind.Mute), Stations).Action);
        Assert.Equal(RadioAudioAction.Unmute, RadioAudioPlan.Step(new RadioEvent(RadioEventKind.Unmute), Stations).Action);
        Assert.Equal(RadioAudioAction.None, RadioAudioPlan.Step(new RadioEvent(RadioEventKind.StopTape), Stations).Action);
    }
}
