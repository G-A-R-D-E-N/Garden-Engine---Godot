using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Settings;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ReduceMotionCameraTests
{
    [Fact]
    public void A_power_armor_transition_pulls_the_camera_back_unless_motion_is_reduced()
    {
        var normal = new PowerArmorCameraTransition(1, 0.5, 2);
        normal.Begin(false);
        normal.Tick(0.5);

        var reduced = new PowerArmorCameraTransition(1, 0.5, 2) { Reduced = true };
        reduced.Begin(false);
        reduced.Tick(0.5);

        Assert.True(normal.Offset.Up > 0 && normal.Offset.Back > 0);
        Assert.Equal((0.0, 0.0), reduced.Offset);
    }

    [Fact]
    public void Reduced_motion_keeps_the_transition_timing_so_the_movement_lock_is_unchanged()
    {
        var reduced = new PowerArmorCameraTransition(1, 0.5, 2) { Reduced = true };
        reduced.Begin(true);
        reduced.Tick(0.5);

        Assert.True(reduced.Active);
        Assert.Equal(0.5, reduced.Progress, 6);

        reduced.Tick(0.6);

        Assert.False(reduced.Active);
    }

    [Fact]
    public void Turning_reduced_motion_on_mid_transition_stops_the_pullback_at_once()
    {
        var t = new PowerArmorCameraTransition(1, 0.5, 2);
        t.Begin(true);
        t.Tick(0.2);
        Assert.True(t.Pullback > 0);

        t.Reduced = true;

        Assert.Equal(0, t.Pullback);
    }

    [Fact]
    public void Weapon_kick_is_scaled_down_but_not_removed_when_motion_is_reduced()
    {
        Assert.Equal(1, SettingsEffects.KickScale(false));
        Assert.Equal(SettingsEffects.ReducedMotionKickFactor, SettingsEffects.KickScale(true));
        Assert.InRange(SettingsEffects.ReducedMotionKickFactor, 0.01, 0.99);
    }
}
