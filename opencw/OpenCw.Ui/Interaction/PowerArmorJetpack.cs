using System;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread; Tick once per physics step. Returns the vertical speed (FO4 units per second) to hold, or null when not thrusting.
public sealed class PowerArmorJetpack(PowerArmorJetpackSettings settings)
{
    private double _time;

    public double Fuel { get; private set; } = Math.Max(0, settings.FuelCapacity);

    public bool Thrusting { get; private set; }

    public double FuelFraction => settings.FuelCapacity > 0 ? Math.Clamp(Fuel / settings.FuelCapacity, 0, 1) : 0;

    // Invariant: a press on the ground (takeoff) or a hold in the air starts a burst; running dry needs a landing to refill before the next one.
    public double? Tick(double dt, bool held, bool pressed, bool onFloor)
    {
        if (!(dt > 0) || !double.IsFinite(dt)) return null;
        if (onFloor && !Thrusting) Fuel = Math.Min(settings.FuelCapacity, Fuel + settings.RefillPerSecond * dt);
        if (!settings.Enabled || !held)
        {
            Thrusting = false;
            return null;
        }
        if (!Thrusting)
        {
            if (!(pressed || !onFloor) || Fuel < settings.MinFuelRequired || Fuel <= 0) return null;
            Thrusting = true;
            _time = onFloor || !settings.InitialOnlyOnTakeoff ? 0 : settings.TimeToSustained;
        }
        var initial = _time < settings.TimeToSustained;
        Fuel -= (initial ? settings.DrainInitial : settings.DrainSustained) * dt;
        _time += dt;
        if (Fuel <= 0)
        {
            Fuel = 0;
            Thrusting = false;
            return null;
        }
        return initial ? settings.ThrustInitial : settings.ThrustSustained;
    }
}
