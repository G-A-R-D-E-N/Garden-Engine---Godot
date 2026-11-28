namespace OpenCommonwealth.Services.Interaction;

public static class DoorVisual
{
    // Constraint: set by DoorInteraction.SwingEnabled; while true the swing moves the door, so the placeholder hide never applies.
    public static bool SwingReplacesPlaceholder { get; set; }

    // Invariant: load doors never change here; a door is only shown again when this placeholder hid it, so a disabled door stays hidden.
    public static bool ShouldToggle(bool loadDoor, bool open, bool visible, bool hiddenByDoor) =>
        ShouldToggle(loadDoor, open, visible, hiddenByDoor, SwingReplacesPlaceholder);

    public static bool ShouldToggle(bool loadDoor, bool open, bool visible, bool hiddenByDoor, bool swingReplaces) =>
        !swingReplaces && !loadDoor && (open ? visible : !visible && hiddenByDoor);

    // Invariant: a door with no recorded state toggles freely only when no load order exists; a reload in progress must not bypass the lock logic.
    public static bool TogglesWithoutRecords(bool hasLoadOrder, bool sourcesAvailable) => !hasLoadOrder && !sourcesAvailable;
}
