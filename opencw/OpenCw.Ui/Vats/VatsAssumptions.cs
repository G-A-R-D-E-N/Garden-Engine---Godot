namespace OpenCommonwealth.Services.Ui.Vats;

public static class VatsAssumptions
{
    // Invariant: lower bounds, not engine values; accuracy 0 adds nothing to the weapon term and Perception 1 is the lowest SPECIAL.
    public const float ConservativeWeaponAccuracy = 0f;
    public const float ConservativePerception = 1f;

    // Invariant: UNVERIFIED placeholder pacing, not an engine value; the seconds between two queued shots during playback.
    public const double ShotIntervalSeconds = 0.6;

    // Invariant: UNVERIFIED placeholder, not the engine's VATS time scale; the fraction of normal speed the world runs at while the VATS menu is open.
    public const double SlowTimeScale = 0.1;

    public static double RealDelta(double delta, double timeScale) =>
        double.IsFinite(timeScale) && timeScale > 0 ? delta / timeScale : delta;

    // Requires: weaponCost is the WEAP Action Point Cost; whole points rounded up, null when the weapon has no usable cost (the menu refuses a cost of 0).
    public static int? ApCost(double weaponCost) =>
        double.IsFinite(weaponCost) && weaponCost > 0 && weaponCost <= int.MaxValue - 1 ? (int)Math.Ceiling(weaponCost) : null;
}
