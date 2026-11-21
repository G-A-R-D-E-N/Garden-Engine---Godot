using System;

namespace OpenCommonwealth.Services.Interaction;

// Constraint: defaults are UNVERIFIED (see code notes).
public sealed class PowerArmorCameraTransition(double seconds = PowerArmorCameraTransition.DefaultSeconds, double pullbackUp = PowerArmorCameraTransition.DefaultPullbackUp, double pullbackBack = PowerArmorCameraTransition.DefaultPullbackBack)
{
    public const double DefaultSeconds = 1.2;
    public const double DefaultPullbackUp = 0.5;
    public const double DefaultPullbackBack = 1.8;

    private double _elapsed;
    private bool _entering;

    public bool Active { get; private set; }

    public bool Entering => _entering;

    public double Progress => Active ? Math.Clamp(_elapsed / seconds, 0, 1) : 1;

    public bool Reduced { get; set; }

    public double Pullback => !Active || Reduced ? 0 :_entering ? 1 - Smooth(Progress) : Math.Sin(Math.PI * Smooth(Progress));

    public (double Up, double Back) Offset => (pullbackUp * Pullback, pullbackBack * Pullback);

    public void Begin(bool entering)
    {
        _entering = entering;
        _elapsed = 0;
        Active = seconds > 0;
    }

    public void Tick(double dt)
    {
        if (!Active || !(dt > 0) || !double.IsFinite(dt)) return;
        _elapsed += dt;
        if (_elapsed >= seconds) Active = false;
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);
}
