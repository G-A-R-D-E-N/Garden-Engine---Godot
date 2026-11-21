namespace OpenCommonwealth.Services.Combat;

public sealed class CriticalMeter
{
    // Invariant: UNVERIFIED placeholders, not engine values; hits that fill the meter and the damage multiplier of a critical shot.
    public const int HitsToFill = 10;
    public const float DamageMultiplier = 2f;

    private int _hits;

    public double Charge => _hits / (double)HitsToFill;

    public bool Ready => _hits >= HitsToFill;

    public void AddHit() => _hits = Math.Min(HitsToFill, _hits + 1);

    // Requires: called when a critical is fired; false and no change unless the meter is full, true empties it.
    public bool TryConsume()
    {
        if (!Ready) return false;
        _hits = 0;
        return true;
    }

    public void Reset() => _hits = 0;
}
