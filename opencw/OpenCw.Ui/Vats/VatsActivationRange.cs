using System;

namespace OpenCommonwealth.Services.Ui.Vats;

public static class VatsActivationRange
{
    public const float Fo4UnitsPerMeter = 69.99125f;
    public const float MaxWeaponMeters = 512f;

    public static float Meters(double weaponMaxRangeFo4, float activateRangeMeters)
    {
        if (!double.IsFinite(weaponMaxRangeFo4) || weaponMaxRangeFo4 <= 0)
            return activateRangeMeters;
        var meters = (float)(weaponMaxRangeFo4 / Fo4UnitsPerMeter);
        if (!float.IsFinite(meters) || meters <= 0)
            return activateRangeMeters;
        return Math.Min(meters, MaxWeaponMeters);
    }
}