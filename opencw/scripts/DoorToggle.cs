using Godot;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.UI.Bootstrap;

namespace OpenCommonwealth;

public partial class DoorToggle : Node
{
    private const string LayerMeta = "door_collision_layer";

    private const string HiddenMeta = "door_hidden_by_toggle";

    private const uint InteractionLayer = 1u << 1;

    public void Bind(PlayerInteraction interaction) => interaction.Activated += Activate;

    public void Activate(Node3D reference)
    {
        if (!reference.IsInGroup("doors") || reference.IsInGroup("teleport_doors")) return;
        if (reference.HasMeta(DoorInteraction.OpenMeta))
        {
            if (reference.GetMeta(DoorInteraction.OpenMeta).AsBool() != IsOpen(reference)) Toggle(reference);
        }
        else if (DoorVisual.TogglesWithoutRecords(GameLoader.HasLoadOrder, UiRecordSources.Current() != null)) Toggle(reference);
    }

    public static bool IsOpen(Node3D door) => !door.Visible;

    public static void Apply(Node3D door, bool open)
    {
        if (DoorVisual.ShouldToggle(door.IsInGroup("teleport_doors"), open, door.Visible, door.HasMeta(HiddenMeta))) Toggle(door);
    }

    public static void Toggle(Node3D door)
    {
        bool open = door.Visible;
        door.Visible = !open;
        if (open) door.SetMeta(HiddenMeta, true);
        else door.RemoveMeta(HiddenMeta);
        if (door is CollisionObject3D root) SetLayer(root, open);
        foreach (var body in door.FindChildren("*", "CollisionObject3D", true, false))
            SetLayer((CollisionObject3D)body, open);
    }

    private static void SetLayer(CollisionObject3D collider, bool open)
    {
        if (open)
        {
            collider.SetMeta(LayerMeta, collider.CollisionLayer);
            if (collider.CollisionLayer != 0) collider.CollisionLayer = InteractionLayer;
        }
        else if (collider.HasMeta(LayerMeta))
        {
            collider.CollisionLayer = collider.GetMeta(LayerMeta).AsUInt32();
        }
    }
}
