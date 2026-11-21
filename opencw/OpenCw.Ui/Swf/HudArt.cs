using OpenCommonwealth.Services.Ui.Hud;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class HudArt
{
    public const string Movie = "Interface/HUDMenu.swf";

    private static readonly string[] Hidden =
    {
        "RightMeters_mc/ExplosiveAmmoCount_mc", "RightMeters_mc/PowerArmorLowBatteryWarning_mc",
        "RightMeters_mc/FlashLightWidget_mc", "RightMeters_mc/ActionPointMeter_mc/Optional_mc",
        "BottomCenterGroup_mc/CritMeter_mc", "TopCenterGroup_mc", "HUDNotificationsGroup_mc", "LeftMeters_mc/RadsMeter_mc",
        "LeftMeters_mc/HPMeter_mc/Optional_mc", "LeftMeters_mc/HPMeter_mc/RadsBar_mc", "LeftMeters_mc/LocationText_mc",
        "BottomCenterGroup_mc/CompassWidget_mc/CompassBar_mc", "BottomCenterGroup_mc/CompassWidget_mc/CompassBGHolder_mc",
    };

    public static SwfBindings Build(HudState s)
    {
        var b = new SwfBindings();
        foreach (var p in Hidden)
            b.Hide.Add(p);
        b.FillX["LeftMeters_mc/HPMeter_mc/MeterBar_mc/MeterBarInternal_mc"] = s.HealthFraction;
        b.FillX["RightMeters_mc/ActionPointMeter_mc/MeterBar_mc/MeterBarInternal_mc"] = s.ActionPointsFraction;
        var ammo = s.Ammo;
        if (!ammo.HasWeapon)
            b.Hide.Add("RightMeters_mc/AmmoCount_mc");
        b.Text["RightMeters_mc/AmmoCount_mc/ClipCount_tf"] = SwfBindings.Number(ammo.Clip, 3);
        b.Text["RightMeters_mc/AmmoCount_mc/ReserveCount_tf"] = ammo.Infinite ? "--" : SwfBindings.Number(ammo.Reserve);
        return b;
    }
}
