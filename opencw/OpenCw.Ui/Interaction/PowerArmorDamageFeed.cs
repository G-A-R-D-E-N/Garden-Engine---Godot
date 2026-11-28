namespace OpenCommonwealth.Services.Interaction;

// Requires: main thread; Tick once per player update. Health lost since the last tick is spread over the worn pieces.
public sealed class PowerArmorDamageFeed(PowerArmorSession session)
{
    private double? _last;
    private int _restores = session.RestoreCount;

    public void Reset() => _last = null;

    public void Tick(double health, double maxHealth)
    {
        if (_restores != session.RestoreCount) { _restores = session.RestoreCount; _last = null; }
        if (_last is { } last && last > health && maxHealth > 0)
            session.DamageSpread((last - health) / maxHealth * session.Settings.PartDamageMultiplier);
        _last = health;
    }
}
