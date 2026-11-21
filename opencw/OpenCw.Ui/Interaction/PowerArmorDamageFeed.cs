using System;
using OpenCommonwealth.Services.Combat;

namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread; Tick once per player update. Health lost since the last tick is spread over the worn pieces.
public sealed class PowerArmorDamageFeed(PowerArmorSession session)
{
    private double? _last;
    private int _restores = session.RestoreCount;

    private PlayerVitals? _vitals;
    private Action? _handler;

    public void Reset() => _last = null;

    // Requires: main thread; every health change of the vitals is measured on its own against the frame occupied at that moment.
    public void Attach(PlayerVitals vitals)
    {
        Detach();
        _vitals = vitals;
        _handler = () => Tick(vitals.Health, vitals.MaxHealth);
        vitals.Changed += _handler;
        _last = vitals.Health;
    }

    public void Detach()
    {
        if (_vitals is not null && _handler is not null) _vitals.Changed -= _handler;
        _vitals = null;
        _handler = null;
    }

    public void Tick(double health, double maxHealth)
    {
        if (_restores != session.RestoreCount) { _restores = session.RestoreCount; _last = null; }
        if (_last is { } last && maxHealth > 0 && Math.Min(last, maxHealth) is var floor && floor > health)
            session.DamageSpread((floor - health) / maxHealth * session.Settings.PartDamageMultiplier);
        _last = health;
    }
}
