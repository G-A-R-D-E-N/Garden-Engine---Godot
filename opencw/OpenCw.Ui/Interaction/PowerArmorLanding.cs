using System;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread; Update once per player update with the vertical velocity sampled before the move.
public sealed class PowerArmorLanding
{
    public const double JumpMeters = 3;

    private bool _airborne;
    private double _peak;

    // Invariant: a move longer than JumpMeters plus twice what the previous speed covers in one frame is a teleport, never a fall.
    public static bool Jumped(double distance, double previousSpeed, double dt) =>
        double.IsFinite(distance) && distance > JumpMeters + 2 * (double.IsFinite(previousSpeed) && double.IsFinite(dt) ? Math.Abs(previousSpeed) * Math.Max(dt, 0) : 0);

    public void Reset()
    {
        _airborne = false;
        _peak = 0;
    }

    public double? Update(bool onFloor, double velocityY)
    {
        if (!onFloor)
        {
            _airborne = true;
            if (double.IsFinite(velocityY)) _peak = Math.Max(_peak, -velocityY);
            return null;
        }
        if (!_airborne) return null;
        _airborne = false;
        var speed = _peak;
        _peak = 0;
        return speed > 0 ? speed : null;
    }
}

public static class PowerArmorStomp
{
    public static double Damage(PowerArmorStompSettings settings, double fallSpeed, double distance, bool hostile = true)
    {
        if (!hostile || !settings.Enabled || !double.IsFinite(fallSpeed) || !double.IsFinite(distance) || distance < 0) return 0;
        if (fallSpeed < settings.MinFallSpeed || settings.Radius <= 0 || distance > settings.Radius) return 0;
        return (fallSpeed - settings.MinFallSpeed) * settings.DamagePerSpeed * (1 - distance / settings.Radius);
    }
}
