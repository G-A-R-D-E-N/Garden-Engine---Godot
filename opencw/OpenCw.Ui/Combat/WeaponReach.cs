namespace OpenCommonwealth.Services.Combat;

public static class WeaponReach
{
    public const string SettingId = "fCombatDistance";

    public static double? Meters(double reach, double combatDistance, double unitsPerMeter)
    {
        var meters = reach * combatDistance / unitsPerMeter;
        return double.IsFinite(meters) && meters > 0 ? meters : null;
    }
}
