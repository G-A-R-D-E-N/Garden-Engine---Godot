namespace OpenCommonwealth.Services.Physics;

public enum Placement
{
    Wait,
    Place,
    NeverResident,
    NoFloor,
}

public static class PlacementGate
{
    public static Placement Decide(bool startCellResident, bool floorHit, bool timedOut) =>
        floorHit && startCellResident ? Placement.Place
        : !timedOut ? Placement.Wait
        : startCellResident ? Placement.NoFloor
        : Placement.NeverResident;

    public static bool CanRecoverFall(bool startCellResident, bool floorHit) => startCellResident && floorHit;

    public static string Reason(Placement outcome) => outcome switch
    {
        Placement.NeverResident => "the start cell is not resident",
        Placement.NoFloor => "the start cell was resident but the floor ray found no collision under the start",
        _ => "",
    };
}
