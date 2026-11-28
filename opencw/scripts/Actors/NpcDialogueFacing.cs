using Godot;
using OpenCommonwealth.Services;

namespace OpenCommonwealth;

public static class NpcDialogueFacing
{
    // Requires: puppet and player are in the tree; returns the heading to restore, or null when the turn was refused (seated, posed, walking, already turning) and nothing changed.
    public static Vector3? Begin(Node3D puppet)
    {
        if (!Usable(puppet) || ActorRegistry.Player?.Node is not { } player || !Usable(player)) return null;
        var heading = -puppet.GlobalBasis.Z with { Y = 0f };
        if (!NpcAnimation.TurnTo(puppet, player.GlobalPosition, out var withClip))
        {
            GD.Print($"[Dialogue] {puppet.Name}: not turned to the player (busy, seated, posed or already facing).");
            return null;
        }
        GD.Print($"[Dialogue] {puppet.Name}: turning to the player (clip={withClip}).");
        return heading;
    }

    // Requires: a heading from Begin and the puppet in the tree; see NpcLocomotionStates.RestorePlan for when it turns, snaps or leaves the pose alone.
    public static void End(Node3D puppet, Vector3? heading)
    {
        if (heading is not { } was || !Usable(puppet) || was.LengthSquared() < 1e-6f) return;
        var facing = puppet.GetNodeOrNull<NpcFacing>(NpcFacing.NodeName);
        var locomotion = puppet.GetNodeOrNull<NpcLocomotion>(NpcLocomotion.NodeName);
        bool turnWasRunning = facing?.Turning ?? false;
        bool clip = locomotion?.TurnClipActive ?? false;
        facing?.Cancel();
        bool busy = locomotion != null && NpcLocomotionStates.IsPose(locomotion.State)
            || puppet.GetNodeOrNull<FurniturePlayback>(FurniturePlayback.NodeName) is { State: not FurnitureState.None }
            || puppet.GetNodeOrNull<NavWalker>(NavService.WalkerName) is { Moving: true };
        switch (NpcLocomotionStates.RestorePlan(busy, clip, turnWasRunning))
        {
            case HeadingRestore.Skip:
                GD.Print($"[Dialogue] {puppet.Name}: heading not restored (posed, on furniture or walking).");
                break;
            case HeadingRestore.Snap:
                Snap(puppet, was);
                GD.Print($"[Dialogue] {puppet.Name}: heading snapped back ({(clip ? "a turn clip was still playing" : "the turn to the player was interrupted")}).");
                break;
            default:
                if (!NpcAnimation.TurnTo(puppet, puppet.GlobalPosition + was, out _)) Snap(puppet, was);
                break;
        }
    }

    private static bool Usable(Node3D node) => GodotObject.IsInstanceValid(node) && node.IsInsideTree();

    private static void Snap(Node3D puppet, Vector3 heading)
    {
        var current = -puppet.GlobalBasis.Z with { Y = 0f };
        if (current.LengthSquared() < 1e-6f) return;
        puppet.GlobalBasis = new Basis(Vector3.Up, NpcLocomotionStates.HeadingYaw((float)current.X, (float)current.Z, (float)heading.X, (float)heading.Z)) * puppet.GlobalBasis;
    }
}
