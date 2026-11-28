using Godot;
using System;
using System.Collections.Generic;
using OpenCommonwealth.Lighting;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Lighting;
using OpenCommonwealth.Services.Navigation;
using OpenCommonwealth.Services.Ui.Workshop;

namespace OpenCommonwealth;

public enum PackagePhase
{
    None,
    ToAnchor,
    Holding,
    Dwell,
    Roam,
    Blocked,
    Unreachable,
}

public partial class PackageRunner : Node
{
    public const string NodeName = "PackageRunner";
    public const float RetrySeconds = 2f;
    public const int MaxRetries = 5;
    public const float MaxSnapMeters = 3f;
    private const float FallbackRadiusMeters = 5f; // only for a location record that carries no radius

    public IPackageSource Source { get; private set; } = null!;
    public ActorIdentity Actor { get; private set; } = null!;
    public Func<GameClock?> Clock { get; set; } = () => EnvironmentController.Active?.Clock;
    public ActivePackage? Current { get; private set; }
    public PackagePhase Phase { get; private set; }
    public NavProfile? Profile { get; private set; }
    public string BlockReason { get; private set; } = "";
    public int Retries => _retries;
    public int SeatSlot { get; private set; } = -1;

    private Node3D _puppet = null!;
    private NavWalker? _walker;
    private Random _rng = null!;
    private long _minute = long.MinValue;
    private int _questRevision;
    private int _overrideRevision;
    private long _assignmentRevision;
    private float _timer;
    private Vector3 _home;
    private Vector3? _editorLocation; // constraint: the puppet is placed from its reference record before the runner attaches, so its first tree position is the editor location
    private bool _noted;
    private PackagePhase _tracedPhase = PackagePhase.None;
    private readonly HashSet<string> _failures = new();
    private FurniturePlayback? _furniture;
    private Transform3D? _beforeSeat;
    private bool _seatPending;
    private int _retries;
    private bool _forced;
    private ulong _occupant;

    public void Reevaluate() => _forced = true;

    // Requires: called before the load moves the puppet, because Stop restores the pre-seat transform; the next tick selects again from the loaded quest state.
    public void RestartAfterLoad()
    {
        Stop();
        _minute = long.MinValue;
        _noted = false;
    }

    public static PackageRunner Attach(Node3D puppet, ActorIdentity actor, IPackageSource source, NavProfile? profile = null)
    {
        if (puppet.GetNodeOrNull<PackageRunner>(NodeName) is { } existing) return existing;
        ulong seed = NpcPuppetSpawner.SeedFor(actor.RefKey);
        var runner = new PackageRunner { Name = NodeName, Actor = actor, Source = source, Profile = profile, _rng = new Random(unchecked((int)(seed ^ (seed >> 32)))) };
        puppet.AddChild(runner);
        return runner;
    }

    public override void _EnterTree()
    {
        _puppet = GetParent<Node3D>();
        _occupant = _puppet.GetInstanceId();
        _editorLocation ??= _puppet.GlobalPosition;
        _minute = long.MinValue;
        _questRevision = 0;
        _overrideRevision = 0;
    }

    public override void _ExitTree() => Stop();

    public override void _Process(double delta) => Tick(delta);

    public void Tick(double delta)
    {
        if (!IsInsideTree()) return;
        if (!NpcCrowdBudget.IsActive(_puppet) || CompanionFollower.Controls(_puppet) || NpcCombatDriver.Controls(_puppet))
        {
            if (Current != null || _walker != null) Stop();
            _minute = long.MinValue;
            return;
        }
        if (Clock() is { } clock)
        {
            long minute = (long)Math.Floor(clock.TotalHours * 60.0);
            var runtime = PapyrusRuntime.Find(this);
            int revision = runtime?.QuestRevision ?? 0;
            int overrides = runtime?.SceneHost?.Overrides.Revision ?? 0;
            long assignments = SettlerAssignments.Current.Revision;
            if (minute != _minute || revision != _questRevision || overrides != _overrideRevision || assignments != _assignmentRevision || _forced)
            {
                _forced = false;
                _minute = minute;
                _questRevision = revision;
                _overrideRevision = overrides;
                _assignmentRevision = assignments;
                Reselect(clock);
            }
        }
        if (_seatPending && _furniture?.State == FurnitureState.None)
        {
            _seatPending = false;
            RestoreSeat();
            Begin();
        }
        Advance((float)delta);
        FeedLocomotion();
    }

    private void Reselect(GameClock clock)
    {
        ActivePackage? next = null;
        try
        {
            var assignments = SettlerAssignments.Current;
            next = PapyrusRuntime.Find(this)?.SceneHost?.Overrides.Of(Actor.RefKey)
                ?? SettlerAssignmentPolicy.Apply(Source.Active(Actor, clock),
                    assignments.WorkObjectOf(Actor.RefKey), assignments.BedOf(Actor.RefKey),
                    key => Resolve(new PackageAnchor(RefKey: key)) != null);
        }
        catch (Exception ex)
        {
            if (_failures.Add($"{ex.GetType().Name}: {ex.Message}")) GD.PrintErr($"[Package] {Actor.RefKey}: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        if (next == Current)
        {
            if (Phase != PackagePhase.Unreachable) return;
            _retries = 0;
            Begin();
            return;
        }
        _retries = 0;
        _walker?.Stop();
        Locomotion()?.Release();
        Current = next;
        _noted = false;
        GD.Print($"[Package] {Actor.RefKey}: selected " + (next is null ? "none (no package applies or the list is undecided)"
            : $"{next.Id} kind={next.Kind} procedure={next.Procedure} anchor={(next.Anchor is null ? "none" : $"ref={next.Anchor.RefKey} pos={next.Anchor.Position} editorLocation={next.Anchor.EditorLocation}")} radius={next.RadiusMeters}"));
        if (_furniture is { State: not FurnitureState.None })
        {
            _furniture.RequestExit(false);
            _seatPending = true;
            return;
        }
        _seatPending = false;
        RestoreSeat();
        Begin();
    }

    private void Begin()
    {
        BeginCore();
        if (Phase == _tracedPhase) return;
        _tracedPhase = Phase;
        GD.Print($"[Package] {Actor.RefKey}: {StatusLine()}");
    }

    public string StatusLine()
    {
        var anchor = Current?.Anchor is { } a ? $"ref={a.RefKey} editorLocation={a.EditorLocation}" : "none";
        var nav = Phase switch
        {
            PackagePhase.Blocked => $"blocked retry {_retries}/{MaxRetries} ({BlockReason})",
            PackagePhase.Unreachable => $"unreachable after {MaxRetries} retries ({BlockReason})",
            _ => _walker is { Moving: true } ? "walking" : "idle",
        };
        return $"phase={Phase} package={Current?.Id ?? "none"} kind={Current?.Kind} anchor={anchor} home={_home} nav={nav} seatSlot={SeatSlot}";
    }

    private void BeginCore()
    {
        Phase = PackagePhase.None;
        if (Current is not { } package) return;
        if (package.Kind is PackageProcedureKind.Stay or PackageProcedureKind.Unsupported)
        {
            if (package.Kind == PackageProcedureKind.Unsupported) GD.Print($"[Package] {Actor.RefKey}: stub, procedure of {package.Id} is not executed; holding position.");
            Phase = PackagePhase.Holding;
            return;
        }
        Vector3? target = null;
        if (package.Anchor != null)
        {
            target = Resolve(package.Anchor) is { } raw ? package.Anchor.EditorLocation ? Snap(raw) ?? raw : Snap(raw) : null;
            if (target == null)
            {
                Note($"stub, anchor of {package.Id} did not resolve to a reachable navmesh position; retrying.");
                Block("the anchor did not resolve to a position on the navmesh");
                return;
            }
        }
        else if (package.Kind == PackageProcedureKind.Travel) Note($"stub, travel package {package.Id} has no anchor; holding position.");
        _home = target ?? _puppet.GlobalPosition;
        if (target is { } to && _puppet.GlobalPosition.DistanceTo(to) > NavService.ReachTolerance)
        {
            if (WalkTo(to, PackagePhase.ToAnchor)) return;
            Block("no navmesh route to the anchor");
            return;
        }
        Arrive();
    }

    private void Block(string reason)
    {
        BlockReason = reason;
        if (++_retries > MaxRetries)
        {
            Phase = PackagePhase.Unreachable;
            return;
        }
        Phase = PackagePhase.Blocked;
        _timer = RetrySeconds;
    }

    private void Arrive()
    {
        if (Current is not { } package) return;
        if (package.Kind is PackageProcedureKind.Sandbox or PackageProcedureKind.Wander)
        {
            _retries = 0;
            Phase = PackagePhase.Dwell;
            _timer = DwellSeconds(package.Kind);
            return;
        }
        Phase = PackagePhase.Holding;
        if (package.Kind is not (PackageProcedureKind.Sleep or PackageProcedureKind.Eat or PackageProcedureKind.Sit))
        {
            _retries = 0;
            return;
        }
        var pose = package.Kind switch { PackageProcedureKind.Sleep => NpcLocoState.Sleep, PackageProcedureKind.Eat => NpcLocoState.Use, _ => NpcLocoState.Sit };
        var seat = (SeatSource ?? TakeSeat)(package, pose);
        if (seat == SeatResult.NoSeat)
        {
            Block("every slot of the furniture is taken or out of reach");
            return;
        }
        _retries = 0;
        if (seat == SeatResult.NoFurniture && Locomotion()?.Request(pose) != true)
            GD.Print($"[Package] {Actor.RefKey}: stub, {package.Id} has no furniture slot at its anchor and no {pose} pose clip; standing at the anchor.");
    }

    public enum SeatResult { NoFurniture, NoSeat, Seated }

    public Func<ActivePackage, NpcLocoState, SeatResult>? SeatSource { get; set; }

    public ulong Occupant => _occupant;

    private SeatResult TakeSeat(ActivePackage package, NpcLocoState pose)
    {
        if (package.Anchor?.RefKey is not { } key || PackageRefIndex.Find(GetTree().Root, key) is not { } furniture
            || FurnitureLookup.Of(furniture)?.Slots is not { Count: > 0 }) return SeatResult.NoFurniture;
        var furnitureId = FurnitureLookup.OccupancyKey(furniture);
        if (FurnitureLookup.SeatFor(furniture, _puppet.GlobalPosition, slot => FurnitureOccupancy.Shared.IsFree(furnitureId, slot, _occupant)) is not { } seat)
            return SeatResult.NoSeat;
        FurnitureOccupancy.Shared.TryReserve(furnitureId, seat.Slot, _occupant);
        SeatSlot = seat.Slot;
        var clips = FurnitureClipSource.Prepare(_puppet, furniture);
        _beforeSeat = _puppet.GlobalTransform;
        var rotation = _puppet.GlobalRotation;
        rotation.Y = (float)seat.Yaw;
        _puppet.GlobalPosition = seat.Position;
        _puppet.GlobalRotation = rotation;
        _furniture ??= FurniturePlayback.Ensure(_puppet);
        var clip = clips != null && _furniture.Begin(clips) ? "furniture enter/loop/exit clips" : Locomotion()?.Request(pose) == true ? $"locomotion {pose} pose" : "none";
        var missing = clip == "furniture enter/loop/exit clips" ? "nothing" : clips == null ? "furniture clips (reason in the stub line above)" : "furniture clips (not loadable)";
        if (clip == "none") missing += $", locomotion {pose} clip";
        GD.Print($"[Package] {Actor.RefKey}: seated slot {seat.Slot} of {furniture.Name} by transform; animation={clip}; missing={missing}.");
        return SeatResult.Seated;
    }

    private void RestoreSeat()
    {
        FurnitureOccupancy.Shared.Release(_occupant);
        SeatSlot = -1;
        if (_beforeSeat is not { } back) return;
        _beforeSeat = null;
        if (_puppet != null && GodotObject.IsInstanceValid(_puppet) && _puppet.IsInsideTree()) _puppet.GlobalTransform = back;
    }

    private void Advance(float delta)
    {
        if (Phase is not (PackagePhase.Dwell or PackagePhase.Blocked)) return;
        _timer -= delta;
        if (_timer > 0f) return;
        if (Phase == PackagePhase.Blocked) Begin();
        else Roam();
    }

    private void Roam()
    {
        if (Current is not { } package) return;
        float radius = package.RadiusMeters > 0f ? package.RadiusMeters : FallbackRadiusMeters;
        float angle = (float)(_rng.NextDouble() * Math.Tau);
        float distance = radius * MathF.Sqrt((float)_rng.NextDouble());
        var point = _home + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance;
        var snapped = NavigationServer3D.MapGetClosestPoint(_puppet.GetWorld3D().NavigationMap, point);
        if (snapped.DistanceTo(_home) <= radius + NavService.ReachTolerance && WalkTo(snapped, PackagePhase.Roam)) return;
        _timer = DwellSeconds(package.Kind);
    }

    private float DwellSeconds(PackageProcedureKind kind) =>
        kind == PackageProcedureKind.Wander ? 0.5f + (float)_rng.NextDouble() * 1.5f : 3f + (float)_rng.NextDouble() * 5f;

    private bool WalkTo(Vector3 target, PackagePhase phase)
    {
        var speed = _puppet.GetNodeOrNull<NpcLocomotion>(NpcLocomotion.NodeName)?.WalkSpeed ?? NavWalker.DefaultSpeed;
        var walker = NavService.WalkTo(_puppet, target, speed, Profile);
        if (walker == null) return false;
        if (_walker != walker)
        {
            _walker = walker;
            walker.Arrived += OnArrived;
            walker.Failed += OnFailed;
        }
        Phase = phase;
        return true;
    }

    private void OnArrived()
    {
        if (Phase is not (PackagePhase.ToAnchor or PackagePhase.Roam)) return;
        Locomotion()?.SetVelocity(Vector3.Zero);
        Arrive();
    }

    private void OnFailed()
    {
        if (Phase is not (PackagePhase.ToAnchor or PackagePhase.Roam)) return;
        Block("the walker could not reach the target");
    }

    private Vector3? Resolve(PackageAnchor anchor)
    {
        if (anchor.Position is { } position) return position;
        if (anchor.EditorLocation) return _editorLocation;
        if (anchor.RefKey is not { } key) return null;
        if (ActorRegistry.Find(key)?.Node is { } actor) return actor.GlobalPosition;
        return PackageRefIndex.Find(GetTree().Root, key)?.GlobalPosition;
    }

    private Vector3? Snap(Vector3 point)
    {
        var snapped = NavigationServer3D.MapGetClosestPoint(_puppet.GetWorld3D().NavigationMap, point);
        return snapped.DistanceTo(point) <= MaxSnapMeters ? snapped : null;
    }

    private void Note(string message)
    {
        if (_noted) return;
        _noted = true;
        GD.Print($"[Package] {Actor.RefKey}: {message}");
    }

    private NpcLocomotion? Locomotion() => _puppet.GetNodeOrNull<NpcLocomotion>(NpcLocomotion.NodeName);

    private void FeedLocomotion()
    {
        if (_puppet.GetNodeOrNull<NpcLocomotion>(NpcLocomotion.NodeName) is not { } locomotion) return;
        locomotion.SetVelocity(_walker is { Moving: true } walker && GodotObject.IsInstanceValid(walker) ? new Vector3(walker.Speed, 0f, 0f) : Vector3.Zero);
    }

    private void Stop()
    {
        if (_furniture != null && GodotObject.IsInstanceValid(_furniture)) _furniture.Reset();
        RestoreSeat();
        _seatPending = false;
        if (_walker != null && GodotObject.IsInstanceValid(_walker))
        {
            _walker.Arrived -= OnArrived;
            _walker.Failed -= OnFailed;
            _walker.Stop();
        }
        _walker = null;
        if (_puppet != null && GodotObject.IsInstanceValid(_puppet) && _puppet.GetNodeOrNull<NpcLocomotion>(NpcLocomotion.NodeName) is { } locomotion)
        {
            locomotion.SetVelocity(Vector3.Zero);
            locomotion.Release();
        }
        Current = null;
        Phase = PackagePhase.None;
        _retries = 0;
    }
}
