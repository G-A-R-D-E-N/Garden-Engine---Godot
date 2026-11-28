using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Physics;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workshop;
using OpenCommonwealth.UI.Bootstrap;

namespace OpenCommonwealth;

// Requires: main thread; runs while the build menu is open (the menu pauses the tree, so the node processes always), turns the player view while Shift is held (every mouse button is bound by the menu input, so no button can gate it), and is freed with the menu.
public partial class WorkshopScene : Node3D
{
    private const float Reach = 8f;
    private const float MinUpright = 0.6f;
    private const float ClearRadius = 0.25f;

    private readonly IRecordReader _reader;
    private readonly WorkshopScrapRules _rules;
    private readonly CharacterBody3D _player;
    private readonly PlayerController? _controller;
    private readonly Func<bool> _isTop;
    private readonly HashSet<string> _unsupported = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _bases = new(StringComparer.Ordinal);
    private readonly Node3D _bench;
    private readonly Func<Vector3, bool> _area;
    private readonly PlayerInteraction? _interaction;
    private readonly CameraRig _rig;
    private PackedScene? _scene;
    private string? _sceneFor;
    private Node3D? _ghost;
    private (Vector3 Size, Vector3 Center)? _footprint;
    private string? _lastToken;
    private Vector3 _point;
    private Node3D? _cell;
    private bool _looking;
    private bool _shiftReleased;
    private ulong _aimedFrame = ulong.MaxValue;
    private Node3D? _aimedNode;
    private WorkshopTarget? _aimedTarget;
    private bool _retryAim;
    private ulong _nextAimReadMs;
    private Node3D? _cardNode;
    private WorkshopSettlerCard? _card;

    public WorkshopScene(IRecordReader reader, IReadOnlyCollection<string> workbenchKeywords, CharacterBody3D player, Node3D bench, Func<bool> isTop, Func<Vector3, bool> area)
    {
        _reader = reader;
        _rules = new WorkshopScrapRules(reader, workbenchKeywords);
        _player = player;
        _controller = player as PlayerController;
        _isTop = isTop;
        _bench = bench;
        _area = area;
        _interaction = player.GetNodeOrNull<PlayerInteraction>("Interaction");
        _rig = player.GetNode<CameraRig>("CameraRig");
        ProcessMode = ProcessModeEnum.Always;
    }

    // Constraint: UiBootstrap.OnTopChanged owns the mouse mode when the menu closes, so a held look only hands the cursor back if another menu stays open.
    public override void _ExitTree()
    {
        if (_looking) Callable.From(() => { if (UiBootstrap.Instance?.Ui is { IsAnyMenuOpen: true }) Input.MouseMode = Input.MouseModeEnum.Visible; }).CallDeferred();
        _looking = false;
        ClearGhost();
    }

    public override void _Process(double delta)
    {
        var shift = Input.IsKeyPressed(Key.Shift);
        if (!shift) _shiftReleased = true;
        var want = _isTop() && shift && _shiftReleased;
        if (want && !_looking) BeginLook();
        else if (!want) EndLook();
    }

    public override void _Input(InputEvent @event)
    {
        if (_looking && @event is InputEventMouseMotion motion) _controller?.LookBy(motion.Relative);
    }

    private void BeginLook()
    {
        _looking = true;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void EndLook()
    {
        if (!_looking) return;
        _looking = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public PlacementStatus Check(string recipeId)
    {
        var baseKey = BaseOf(recipeId);
        if (baseKey is null || !EnsureGhost(recipeId, baseKey)) { FreeGhost(); return PlacementStatus.Unsupported; }
        var camera = _rig.Camera.GlobalTransform;
        var from = _rig.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(from, from - camera.Basis.Z * Reach);
        query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
        query.CollisionMask = PhysicsLayers.FlightMask;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || ((Vector3)hit["normal"]).Y < MinUpright)
        {
            _ghost!.Visible = false;
            return PlacementStatus.NeedsTerrain;
        }
        var groundPoint = (Vector3)hit["position"];
        _cell = CellOf(hit["collider"].As<Node>()) ?? CellOf(_bench);
        _ghost!.Visible = true;
        _ghost.GlobalRotation = new Vector3(0f, camera.Basis.GetEuler().Y, 0f);
        var footprint = _footprint;
        if (WorkshopFootprint.PlacementOrigin(groundPoint, _ghost.GlobalBasis, footprint, _area) is not { } origin)
        {
            _ghost.Visible = false;
            return PlacementStatus.OutsideArea;
        }
        _point = origin;
        _ghost.GlobalPosition = _point;
        if (_cell is null) return PlacementStatus.Unsupported;
        if (footprint is { } sized && !FootprintSupported(_ghost.GlobalTransform, sized)) return PlacementStatus.NeedsTerrain;
        var overlap = new PhysicsShapeQueryParameters3D
        {
            Shape = footprint is { } probe ? new BoxShape3D { Size = probe.Size } : new SphereShape3D { Radius = ClearRadius },
            Transform = footprint is { } at ? _ghost.GlobalTransform * new Transform3D(Basis.Identity, at.Center) : new Transform3D(Basis.Identity, _point + Vector3.Up * (ClearRadius + 0.2f)),
            CollisionMask = PhysicsLayers.FlightMask,
            Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() },
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(overlap, 1).Count > 0 ? PlacementStatus.Blocked : PlacementStatus.Valid;
    }

    private bool FootprintSupported(Transform3D ghost, (Vector3 Size, Vector3 Center) probe)
    {
        var space = GetWorld3D().DirectSpaceState;
        foreach (var point in WorkshopFootprint.SupportPoints(probe))
        {
            var world = ghost * point;
            var ray = PhysicsRayQueryParameters3D.Create(world + Vector3.Up * WorkshopFootprint.SupportRise, world - Vector3.Up * WorkshopFootprint.SupportDrop);
            ray.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
            ray.CollisionMask = PhysicsLayers.FlightMask;
            var hit = space.IntersectRay(ray);
            if (hit.Count == 0 || ((Vector3)hit["normal"]).Y < MinUpright) return false;
        }
        return true;
    }

    private static (Vector3 Size, Vector3 Center)? FootprintOf(Node3D ghost)
    {
        var inverse = ghost.GlobalTransform.AffineInverse();
        Aabb? merged = null;
        var geometries = ghost.FindChildren("*", "GeometryInstance3D", true, false).Cast<GeometryInstance3D>().ToList();
        if (ghost is GeometryInstance3D root) geometries.Insert(0, root);
        foreach (var geometry in geometries)
            merged = WorkshopFootprint.Include(merged, inverse * geometry.GlobalTransform, geometry.GetAabb());
        var colliders = ghost.FindChildren("*", "CollisionShape3D", true, false).Cast<CollisionShape3D>().ToList();
        if (ghost is CollisionShape3D rootCollider) colliders.Insert(0, rootCollider);
        foreach (var collider in colliders)
            if (!collider.Disabled && collider.Shape?.GetDebugMesh() is { } debug)
                merged = WorkshopFootprint.Include(merged, inverse * collider.GlobalTransform, debug.GetAabb());
        return merged is { } measured ? WorkshopFootprint.Probe(measured) : null;
    }

    public bool Place(string recipeId)
    {
        if (_ghost is not { Visible: true } || _cell is null || BaseOf(recipeId) is not { } baseKey) return false;
        var local = _cell.ToLocal(_point);
        var built = BuiltObjectLedger.Current.Add(_cell.GetMeta(CellCompiler.CellFormKeyMetadata).AsString(), recipeId, baseKey,
            local.X, local.Y, local.Z, _ghost.GlobalRotation.Y - _cell.GlobalRotation.Y);
        if (BuiltObjectSpawner.Spawn(_cell, built) is null)
        {
            BuiltObjectLedger.Current.Remove(built.Token);
            return false;
        }
        _lastToken = built.Token;
        return true;
    }

    public void Undo(string recipeId)
    {
        if (_lastToken is not { } token) return;
        BuiltObjectLedger.Current.Remove(token);
        SettlerAssignments.Current.ObjectGone(token);
        if (_cell is { } cell && cell.GetNodeOrNull(new NodePath("Built_" + token[BuiltObjectLedger.TokenPrefix.Length..])) is { } spawned) spawned.QueueFree();
        _lastToken = null;
    }

    public WorkshopTarget? Aimed()
    {
        var frame = Engine.GetProcessFrames();
        if (frame == _aimedFrame) return _aimedTarget;
        _aimedFrame = frame;
        _interaction?.RefreshAim();
        if (_interaction?.Aimed is not { } node || !Scrappable(node))
        {
            _aimedNode = null;
            return _aimedTarget = null;
        }
        var now = Time.GetTicksMsec();
        if (node != _aimedNode || (now >= _nextAimReadMs && (_retryAim || node.IsInGroup("containers"))))
        {
            _aimedNode = node;
            _nextAimReadMs = now + 1000;
            var key = node.GetMeta("fo4_form_key").AsString();
            var beforeRead = _reader.FailureCount;
            _aimedTarget = _rules.TargetFor(key, node.GetMeta("fo4_base_form_key").AsString(), HoldsItems(node, key));
            _retryAim = beforeRead != _reader.FailureCount;
        }
        return _aimedTarget;
    }

    public WorkshopSettlerCard? AimedSettler()
    {
        Aimed();
        if (_aimedNode is not { } node || !node.IsInGroup("actors"))
        {
            _cardNode = null;
            return _card = null;
        }
        if (node != _cardNode)
        {
            _cardNode = node;
            _card = WorkshopSettlers.CardOf(node, _bench, _reader);
        }
        return _card;
    }

    private bool Scrappable(Node3D node) =>
        GodotObject.IsInstanceValid(node) && node != _bench && !node.IsQueuedForDeletion()
        && node.HasMeta("fo4_form_key") && node.HasMeta("fo4_base_form_key") && _area(node.GlobalPosition);

    private static bool HoldsItems(Node node, string key) => node.IsInGroup("containers") &&
        WorkshopScrapRules.HoldsItems(ContainerLedger.Current.Find(key), node.HasMeta(BuiltObjectSpawner.BuiltMeta), () => RadicleContainerSeed.Stacks(key));

    public bool Scrap(string targetId)
    {
        if (Aimed() is not { } target || target.Id != targetId || _aimedNode is not { } node) return false;
        if (!Scrappable(node) || node.GetMeta("fo4_form_key").AsString() != targetId || HoldsItems(node, targetId)) return false;
        WorkshopScrapRelease.Release(targetId, node.HasMeta(BuiltObjectSpawner.BuiltMeta), node.QueueFree);
        _aimedFrame = ulong.MaxValue;
        _aimedNode = null;
        return true;
    }

    private string? BaseOf(string recipeId)
    {
        if (_bases.TryGetValue(recipeId, out var cached)) return cached;
        var beforeRead = _reader.FailureCount;
        var created = RecordRows.RecipeHeader(_reader, recipeId)?.Created;
        if (beforeRead != _reader.FailureCount) return null;
        return _bases[recipeId] = created;
    }

    private bool EnsureGhost(string recipeId, string baseKey)
    {
        if (_unsupported.Contains(recipeId)) return false;
        if (_sceneFor != recipeId)
        {
            FreeGhost();
            _scene = BuiltObjectSpawner.SceneOf(baseKey);
            _sceneFor = recipeId;
        }
        if (_scene is null) return Unsupported(recipeId);
        if (_ghost is null || !IsInstanceValid(_ghost))
        {
            _ghost = BuiltObjectSpawner.Instance(_scene);
            if (_ghost is null) return Unsupported(recipeId);
            Fade(_ghost);
            AddChild(_ghost);
            _footprint = FootprintOf(_ghost);
        }
        return true;
    }

    private bool Unsupported(string recipeId)
    {
        _unsupported.Add(recipeId);
        return false;
    }

    private void FreeGhost()
    {
        if (_ghost is { } ghost && IsInstanceValid(ghost)) ghost.QueueFree();
        _ghost = null;
    }

    public void ClearGhost()
    {
        FreeGhost();
        _sceneFor = null;
    }

    private static void Fade(Node node)
    {
        if (node is GeometryInstance3D mesh) mesh.Transparency = 0.5f;
        if (node is CollisionObject3D body) body.CollisionLayer = 0;
        foreach (var child in node.GetChildren()) Fade(child);
    }

    public static Node3D? CellOf(Node? node)
    {
        for (; node is not null; node = node.GetParent())
            if (node is Node3D spatial && spatial.HasMeta(CellCompiler.CellFormKeyMetadata)) return spatial;
        return null;
    }
}
