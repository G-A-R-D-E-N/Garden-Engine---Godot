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
}
