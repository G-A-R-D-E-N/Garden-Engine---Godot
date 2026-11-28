namespace OpenCommonwealth.Services.Ui.Vats;

public static class VatsAssumptions
{
    // Invariant: lower bounds, not engine values; accuracy 0 adds nothing to the weapon term and Perception 1 is the lowest SPECIAL.
    public const float ConservativeWeaponAccuracy = 0f;
    public const float ConservativePerception = 1f;

    // Invariant: UNVERIFIED placeholder pacing, not an engine value; the seconds between two queued shots during playback.
    public const double ShotIntervalSeconds = 0.6;

    // Requires: weaponCost is the WEAP Action Point Cost; whole points rounded up, null when the weapon has no usable cost (the menu refuses a cost of 0).
    public static int? ApCost(double weaponCost) =>
        double.IsFinite(weaponCost) && weaponCost > 0 && weaponCost <= int.MaxValue - 1 ? (int)Math.Ceiling(weaponCost) : null;
}
