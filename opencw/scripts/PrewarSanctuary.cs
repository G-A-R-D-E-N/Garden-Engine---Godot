using Godot;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Physics;
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
    public string? PlacementFailure { get; private set; }

    private WorldspaceStreamer _streamer = null!;
    private PlayerController _player = null!;
    private CanvasLayer _loading = null!;
    private SceneTree? _tree;
    private float _yaw;
    private double _waited;
    private double _stable;
    private int _lastResident = -1;
    private bool _fallRecoveryRefused;

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
        if (_fallRecoveryRefused || _player.GlobalPosition.Y < Start.Y - FallLimit)
            RecoverFromFall();
    }

    private void RecoverFromFall()
    {
        var startCell = WorldGrid.ToGrid(Start);
        bool resident = _streamer.IsCellResident(startCell.X, startCell.Y);
        var query = PhysicsRayQueryParameters3D.Create(Start + Vector3.Up * 1.2f, Start + Vector3.Down * 3);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!PlacementGate.CanRecoverFall(resident, hit.Count > 0))
        {
            if (!_fallRecoveryRefused)
            {
                GD.PrintErr("[Sanctuary] fall recovery refused: the start cell or floor is not ready; the player stays disabled.");
                _loading.GetNode<Label>("Text").Text = "RETURNING TO SAFETY\nWaiting for the start area to load.";
                _loading.Visible = true;
            }
            _fallRecoveryRefused = true;
            _player.ProcessMode = ProcessModeEnum.Disabled;
            _streamer.FocusOverride = Start;
            return;
        }
        var point = (Vector3)hit["position"];
        GD.Print("[Sanctuary] player fell out of the world, returning to the start floor.");
        _player.TeleportTo(point + Vector3.Up * 0.05f, _yaw);
        _player.ProcessMode = ProcessModeEnum.Inherit;
        _streamer.FocusOverride = null;
        _loading.Visible = false;
        _fallRecoveryRefused = false;
    }

    private void WaitForCells(double delta)
    {
        _waited += delta;
        int resident = _streamer.ResidentCount;
        var startCell = WorldGrid.ToGrid(Start);
        bool startResident = _streamer.IsCellResident(startCell.X, startCell.Y);
        _stable = resident == _lastResident ? _stable + delta : 0;
        _lastResident = resident;

        bool settled = resident >= MinResidentCells && _stable >= SettleSeconds
            && _streamer.QueuedCount == 0 && _streamer.InflightCount == 0;
        if (!settled && _waited < GiveUpSeconds) return;

        var query = PhysicsRayQueryParameters3D.Create(Start + Vector3.Up * 1.2f, Start + Vector3.Down * 3);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        var outcome = PlacementGate.Decide(startResident, hit.Count > 0, _waited >= GiveUpSeconds);
        if (outcome == Placement.Wait) return;
        if (outcome != Placement.Place) { Fail(resident, PlacementGate.Reason(outcome)); return; }

        var point = (Vector3)hit["position"];
        GD.Print($"[Sanctuary] {resident} cells resident after {_waited:0.0}s; floor found at y={point.Y:0.00} (start y={Start.Y:0.00}) on {hit["collider"].As<Node>()?.GetParent()?.Name}.");
        _player.TeleportTo(point + Vector3.Up * 0.05f, _yaw);
        _player.ProcessMode = ProcessModeEnum.Inherit;
        _streamer.LoadingMode = false;
        _loading.Visible = false;
        PlacementFailure = null;
        Placed = true;
    }

    private void Fail(int resident, string reason)
    {
        var first = PlacementFailure is null;
        PlacementFailure = $"{reason} ({resident} cells resident, {_streamer.QueuedCount} queued, {_streamer.InflightCount} in flight, {_waited:0}s)";
        if (!first) return;
        GD.PrintErr($"[Sanctuary] PLACEMENT FAILED: {PlacementFailure}; the player stays disabled and placement keeps retrying.");
        _loading.GetNode<Label>("Text").Text = $"COULD NOT PLACE THE PLAYER\n{PlacementFailure}";
    }
}
