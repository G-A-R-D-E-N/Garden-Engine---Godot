using System.Globalization;

namespace OpenCommonwealth.Services.Combat;

public enum WeaponKind { Other, Melee, Ranged, Thrown, Mine }

public sealed record WeaponRecoil(
    double MinPerShot, double MaxPerShot, double HipMult, double DiminishSpringForce, double DiminishSightsMult,
    double Arc, double ArcRotate, int RunawayShots)
{
    public double Magnitude(double t) => Math.Min(MinPerShot, MaxPerShot) + Math.Abs(MaxPerShot - MinPerShot) * Math.Clamp(double.IsFinite(t) ? t : 0, 0, 1);

    public double PitchKickRadians(double t, bool aiming) =>
        Magnitude(t) * (aiming || HipMult <= 0 ? 1 : HipMult) * Math.PI / 180;
}

public sealed record WeaponInfo(
    int AnimationType, string? AmmoKey, int Capacity, double BaseDamage, double Reach, double MaxRange,
    int ProjectileCount, uint Flags, double FireSeconds, double ReloadSeconds, double AttackSeconds,
    double AmmoDamage = 0, WeaponRecoil? Recoil = null, double Speed = 0, double ActionPointCost = 0)
{
    private const string Prefix = "WEAP\\";

    public double Scaled(double seconds) => double.IsFinite(Speed) && Speed > 0 ? seconds / Speed : seconds;

    public WeaponKind Kind => AnimationType switch
    {
        >= 0 and <= 6 => WeaponKind.Melee,
        7 or 8 or 9 => WeaponKind.Ranged,
        10 => WeaponKind.Thrown,
        11 => WeaponKind.Mine,
        _ => WeaponKind.Other,
    };

    public bool Automatic => (Flags & 1u << 15) != 0;
    public bool NotPlayable => (Flags & 1u << 20) != 0;

    public int Pellets => Math.Max(1, ProjectileCount);

    public static WeaponInfo Read(Func<string, string?> field, Func<string, string?> link, Func<string, double>? ammoDamage = null,
        Func<string, string, string?>? linkedField = null)
    {
        double D(string name) => Num(field(Prefix + name));
        var ammo = link(Prefix + "Ammo");
        return new WeaponInfo(
            (int)D("AnimationType"), ammo, (int)D("Capacity"), D("BaseDamage"), D("Reach"), D("MaxRange"),
            (int)D("ProjectileCount"), (uint)D("Flags"), D("AnimationFireSeconds"), D("AnimationReloadSeconds"),
            D("AnimationAttackSeconds"), ammo is null ? 0 : ammoDamage?.Invoke(ammo) ?? 0, ReadRecoil(link(Prefix + "AimModel"), linkedField), D("Speed"), D("ActionPointCost"));
    }

    private static WeaponRecoil? ReadRecoil(string? aim, Func<string, string, string?>? linkedField)
    {
        if (aim is null || linkedField is null) return null;
        string? F(string name) => linkedField(aim, "AMDL\\" + name);
        if (F("RecoilMinPerShot") is null && F("RecoilMaxPerShot") is null) return null;
        return new WeaponRecoil(
            Num(F("RecoilMinPerShot")), Num(F("RecoilMaxPerShot")), Num(F("RecoilHipMult")),
            Num(F("RecoilDiminishSpringForce")), Num(F("RecoilDiminishSightsMult")), Num(F("RecoilArc")),
            Signed(F("RecoilArcRotate")), (int)Num(F("RunawayRecoilShots")));
    }

    private static double Signed(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : 0;

    private static double Num(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) && v >= 0 ? v : 0;
}
