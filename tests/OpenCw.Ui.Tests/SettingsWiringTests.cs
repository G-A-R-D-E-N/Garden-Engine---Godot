using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SettingsWiringTests
{
    [Theory]
    [InlineData(0.7, 3)]
    [InlineData(1.0, 3)]
    [InlineData(0.5, 2)]
    [InlineData(0.3, 1)]
    [InlineData(0.1, 1)]
    [InlineData(double.NaN, 3)]
    public void View_distance_maps_to_a_stream_radius_that_never_exceeds_the_streamer_default(double fraction, int radius)
    {
        Assert.Equal(radius, SettingsEffects.StreamRadius(fraction));
    }

    [Fact]
    public void The_view_distance_slider_maps_to_the_effect_the_streamer_consumes()
    {
        var effect = Assert.IsType<SetViewDistance>(Assert.Single(SettingsEffects.Map("graphics.view_distance", SettingValue.Slider(50))));
        Assert.Equal(2, SettingsEffects.StreamRadius(effect.Fraction));
    }

    [Fact]
    public void Autosave_fires_once_per_interval_of_idle_time()
    {
        var timer = new AutosaveTimer();

        Assert.False(timer.Tick(299, 5, true, true));
        Assert.True(timer.Tick(1, 5, true, true));
        Assert.False(timer.Tick(1, 5, true, true));
    }

    [Fact]
    public void Autosave_time_does_not_accumulate_while_busy_and_restarts_when_disabled()
    {
        var timer = new AutosaveTimer();
        Assert.False(timer.Tick(200, 5, true, true));
        Assert.False(timer.Tick(1000, 5, true, false));
        Assert.False(timer.Tick(50, 5, false, true));

        Assert.False(timer.Tick(299, 5, true, true));
        Assert.True(timer.Tick(1, 5, true, true));
    }

    [Fact]
    public void A_shorter_interval_applies_to_time_already_elapsed()
    {
        var timer = new AutosaveTimer();
        Assert.False(timer.Tick(250, 20, true, true));

        Assert.True(timer.Tick(50, 5, true, true));
    }

    [Fact]
    public void Rumble_fires_on_a_rise_of_the_damage_flash_only()
    {
        var rumble = new HitRumble();

        Assert.False(rumble.Update(0));
        Assert.True(rumble.Update(0.6));
        Assert.False(rumble.Update(0.4));
        Assert.False(rumble.Update(0.2));
        Assert.True(rumble.Update(0.5));
        Assert.False(rumble.Update(0));
    }
}
