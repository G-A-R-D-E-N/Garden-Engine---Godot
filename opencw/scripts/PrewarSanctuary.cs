using Godot;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Worldspace;

namespace OpenCommonwealth;

public partial class PrewarSanctuary : Node3D
{
    public const string WorldspaceName = "SanctuaryHillsWorld";
    private const string FallbackScene = "res://scenes/playercontrollertest.tscn";
    private const int MinResidentCells = 9;
    private const double SettleSeconds = 1.0;
    private const double GiveUpSeconds = 90.0;
    private const float FallLimit = 200f;
    private static readonly Vector3 StartFo4 = new(-80302.86f, 90827.945f, 7856f);
    private const float StartHeading = 2.5376022f;

    public static string ManifestPath => AssetCachePaths.Resolve($"worldspaces/{WorldspaceName}/worldspace.json");
    public static bool IsCompiled => System.IO.File.Exists(ProjectSettings.GlobalizePath(ManifestPath));

    public bool Placed { get; private set; }
    public Vector3 Start { get; private set; }

    private WorldspaceStreamer _streamer = null!;
    private PapyrusRuntime? _papyrus;
    private Node? _audio;
    private PlayerController _player = null!;
    private CanvasLayer _loading = null!;
    private SceneTree? _tree;
    private float _yaw;
    private double _waited;
    private double _stable;
    private int _lastResident = -1;

    public override void _Ready()
    {
        if (!IsCompiled)
        {
            GD.PushWarning($"Pre-war Sanctuary is not compiled ({ManifestPath}); loading the test level instead.");
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, FallbackScene);
            SetPhysicsProcess(false);
            return;
        }

        Start = new Vector3(StartFo4.X, StartFo4.Z, -StartFo4.Y) * WorldScale.Fo4ToGodot;
        _yaw = -StartHeading;

        _player = GetNode<PlayerController>("Player");
        _loading = GetNode<CanvasLayer>("Loading");
        _player.GlobalPosition = Start + Vector3.Up * 3;
        _player.ProcessMode = ProcessModeEnum.Disabled;

        _tree = GetTree();
        _tree.NodeAdded += DisableCollision;

        var doors = new DoorToggle { Name = "DoorToggle" };
        AddChild(doors);
        doors.Bind(_player.GetNode<PlayerInteraction>("Interaction"));

        _streamer = new WorldspaceStreamer
        {
            Name = "WorldspaceStreamer",
            ManifestPath = ManifestPath,
            TargetPath = new NodePath("../Player"),
            FullRadius = 2,
            LoadingMode = true,
        };
        AddChild(_streamer);
        _papyrus = PapyrusRuntime.Ensure(this);
    }

    public override void _ExitTree()
    {
        if (_tree != null) _tree.NodeAdded -= DisableCollision;
    }

    private static void DisableCollision(Node node)
    {
        if (node.ProcessMode != ProcessModeEnum.Disabled) return;
        if (node is CollisionObject3D root) root.CollisionLayer = 0;
        foreach (var body in node.FindChildren("*", "CollisionObject3D", true, false))
            ((CollisionObject3D)body).CollisionLayer = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Placed)
        {
            WaitForCells(delta);
            return;
        }
        _audio ??= _streamer.AttachCellAudio(this, WorldGrid.ToGrid(_player.GlobalPosition), _player);
        if (_player.GlobalPosition.Y < Start.Y - FallLimit)
        {
            GD.Print("[Sanctuary] player fell out of the world, returning to the start.");
            _player.TeleportTo(Start, _yaw);
        }
    }

    private void WaitForCells(double delta)
    {
        _waited += delta;
        int resident = _streamer.ResidentCount;
        _stable = resident == _lastResident ? _stable + delta : 0;
        _lastResident = resident;

        bool settled = resident >= MinResidentCells && _stable >= SettleSeconds
            && _streamer.QueuedCount == 0 && _streamer.InflightCount == 0;
        if (!settled && _waited < GiveUpSeconds) return;

        var query = PhysicsRayQueryParameters3D.Create(Start + Vector3.Up * 1.2f, Start + Vector3.Down * 3);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 && _waited < GiveUpSeconds) return;

        var point = hit.Count > 0 ? (Vector3)hit["position"] : Start;
        GD.Print($"[Sanctuary] {resident} cells resident after {_waited:0.0}s; floor {(hit.Count > 0 ? "found" : "not found")} at y={point.Y:0.00} (start y={Start.Y:0.00}) on {(hit.Count > 0 ? hit["collider"].As<Node>()?.GetParent()?.Name : "-")}.");
        _player.TeleportTo(point + Vector3.Up * 0.05f, _yaw);
        _player.ProcessMode = ProcessModeEnum.Inherit;
        _audio ??= _streamer.AttachCellAudio(this, WorldGrid.ToGrid(point), _player);
        _streamer.LoadingMode = false;
        _loading.Visible = false;
        Placed = true;
    }
}
