using System;

namespace OpenCommonwealth.Services.Combat;

public sealed class DryFireFeedback
{
    // Invariant: UNVERIFIED placeholder pacing; the minimum seconds between two notices so a held trigger does not post every frame.
    public const double IntervalSeconds = 1.5;

    private double _cooldown;

    public void Tick(double dt) => _cooldown = Math.Max(0, _cooldown - Math.Max(0, dt));

    // Requires: last is the result of the latest fire attempt and reserve the ammunition left outside the clip; true at most once per IntervalSeconds, only for an empty weapon with nothing to reload.
    public bool ShouldNotify(FireResult last, int reserve)
    {
        if (last != FireResult.Empty || reserve > 0 || _cooldown > 0) return false;
        _cooldown = IntervalSeconds;
        return true;
    }
}
