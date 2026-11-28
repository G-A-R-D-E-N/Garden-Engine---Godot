using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Navigation;
using OpenCommonwealth.Services.Papyrus.LiveState;
using JsonConvert = Newtonsoft.Json.JsonConvert;
using RadicleKey = Radicle.Interop.FormKey;
using ScriptAttachment = OpenCommonwealth.Services.Papyrus.ScriptAttachment;
using ScriptAttachments = OpenCommonwealth.Services.Papyrus.ScriptAttachments;

namespace OpenCommonwealth;

public static class NpcPuppetSpawner
{
    public const string MarkerGroup = "npc_spawns";
    public const string PuppetsName = "NpcPuppets";
    public const string MarkerMeta = "fo4_marker";
    public const string IdleMeta = "fo4_idle_clip";
    public const string ClipsMeta = "fo4_clips";
    public const string SkeletonMeta = "fo4_skeleton_entry";
    public const string StubsMeta = "fo4_stubs";
    public const string DynamicMeta = "fo4_dynamic_ref";
    public const string ResolvedMeta = "fo4_resolved_npc_key";
    public const string RefKeyMeta = CellTscnWriter.FormKeyMeta;
    public const string EnabledMeta = "fo4_enabled";
    public const string AliveMeta = "fo4_alive";
    public const string BaseKeyMeta = "fo4_base_form_key";
    private const string BaseTypeMeta = "fo4_base_type";
    private const string ChosenScriptsMeta = "fo4_chosen_scripts_npc";

    private static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SpawnRetryBudget SpawnRetries = new(2);
    private static string? _failedKey;
    private static readonly ConditionalWeakTable<Node3D, Node3D> Spawned = new();
    private static readonly ConditionalWeakTable<Node3D, StrongBox<bool>> Applied = new();
    private static readonly HashSet<string> LevelStubbed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ActorLevelResult> LevelCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, float> HealthCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ActorProtection> ProtectionCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, NavProfile> ProfileCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Pins = new(StringComparer.Ordinal);
    private static int _pinSession;
    private static bool _unavailable;

    public static Func<string, ushort, ulong, string?>? LeveledPick { get; set; } = PickLeveledNpc;

    internal static Func<RadicleKey, ushort, ulong, Radicle.Interop.LeveledPickOutcome?> LeveledPickRead { get; set; } = ReadLeveledPick;

    internal static Func<RadicleKey, ActorLevelResult> LevelRead { get; set; } = ActorLevel.Read;

    internal static Func<RadicleKey, float?> HealthRead { get; set; } = ActorHealth.Read;

    internal static Func<RadicleKey, ActorProtection?> ProtectionRead { get; set; } = ActorHealth.ReadProtection;

    internal static Func<RadicleKey, ushort, ulong, NavProfile?> ProfileRead { get; set; } = ReadProfile;

    public static string ScenePath(RadicleKey key, ulong seed) => NpcRigSharing.SeededPath(key, seed);

    public static bool IsEnabled(Node puppet) => puppet.GetMeta(EnabledMeta, true).AsBool();

    public static bool IsAlive(Node puppet) => puppet.GetMeta(AliveMeta, true).AsBool();

    public static ulong SeedFor(string placedRefKey)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in placedRefKey) hash = (hash ^ c) * 1099511628211UL;
        return hash;
    }

    internal static ushort SpawnLevel => PlayerLevelSource.Current;

    private static readonly Queue<Node3D> PendingBakes = new();
    private static readonly ConditionalWeakTable<Node3D, object> Queued = new();
    private static bool _pumping;

    public static int PendingBakeCount => PendingBakes.Count;

    public static int SpawnAll(Node cellRoot)
    {
        ResetBakeState();
        return SpawnCell(cellRoot);
    }

    public static void ResetBakeState()
    {
        Failed.Clear();
        SpawnRetries.Clear();
        NpcRigSharing.Reset();
        LevelStubbed.Clear();
        LevelCache.Clear();
        HealthCache.Clear();
        ProtectionCache.Clear();
        ProfileCache.Clear();
        _unavailable = false;
    }

    public static int SpawnCell(Node cellRoot, bool deferBakes = false)
    {
        try { return SpawnCellCore(cellRoot, deferBakes); }
        catch (Exception ex)
        {
            EngineLiveState.Readiness.Fail(Subsystem.Actors, ex);
            throw;
        }
    }

    private static int SpawnCellCore(Node cellRoot, bool deferBakes)
    {
        if (cellRoot.IsInsideTree()) CompanionCarry.Ensure(cellRoot.GetTree());
        var markers = new List<Node3D>();
        Collect(cellRoot, markers);
        ResolveLeveled(markers);
        if (markers.Count == 0) EngineLiveState.Readiness.Ready(Subsystem.Actors);
        if (markers.Count == 0 || cellRoot.HasNode(PuppetsName)) return 0;
        EngineLiveState.Readiness.Pending(Subsystem.Actors);
        NpcRigSharing.CleanLegacy();

        var puppets = new Node3D { Name = PuppetsName };
        cellRoot.AddChild(puppets);
        int queued = PendingBakes.Count;
        int spawned = markers.Count(marker =>
        {
            var puppet = Spawn(marker, puppets, deferBakes);
            TraceMarker(marker, puppet);
            return puppet != null;
        });
        int pending = Math.Max(0, PendingBakes.Count - queued);
        if (EngineLiveState.Readiness.StatusOf(Subsystem.Actors).State == ReadyState.Pending) EngineLiveState.Readiness.Ready(Subsystem.Actors);
        cellRoot.AddChild(new NpcCrowdBudget { Name = NpcCrowdBudget.NodeName, Puppets = puppets });
        GD.Print($"[NpcSpawn] {spawned} of {markers.Count} NPC marker(s) spawned, {pending} queued for a bake.");
        return spawned;
    }

    private static void TraceMarker(Node3D marker, Node3D? puppet)
    {
        string outcome = puppet != null ? $"spawned puppet={puppet.Name}"
            : !ActorEnableState.IsEnabled(marker) ? "none: marker is initially disabled"
            : Queued.TryGetValue(marker, out _) ? "none: queued for a rig bake"
            : "none: see the stub or error line above";
        GD.Print($"[NpcSpawn] marker {marker.Name} ref={marker.GetMeta(RefKeyMeta, "?").AsString()} base={marker.GetMeta(BaseKeyMeta, "?").AsString()} type={marker.GetMeta(BaseTypeMeta, "?").AsString()} selected={(puppet?.GetMeta(ResolvedMeta, "").AsString() is { Length: > 0 } picked ? picked : "(base)")} -> {outcome}");
    }

    private static void TraceRig(Node3D puppet, string npcKey, NpcClipSet clips, bool locomotion, bool idle, string skeleton)
    {
        string Role(string name, string? entry) => string.IsNullOrEmpty(entry) ? name + "=MISSING" : $"{name}={entry}" + (NpcIdle.LoadClip(entry) == null ? "(not converted)" : "");
        var missing = new[] { ("idle", clips.Idle), ("walk", clips.Walk), ("run", clips.Run), ("turnL", clips.TurnLeft), ("turnR", clips.TurnRight), ("sit", clips.Sit), ("sleep", clips.Sleep), ("use", clips.Use) }
            .Where(r => string.IsNullOrEmpty(r.Item2) || NpcIdle.LoadClip(r.Item2) == null).Select(r => r.Item1).ToList();
        GD.Print($"[NpcSpawn] rig {puppet.Name} npc={npcKey} skeleton={(skeleton.Length > 0 ? skeleton : "?")} locomotion={locomotion} idlePlayer={idle} "
            + string.Join(' ', Role("idle", clips.Idle), Role("walk", clips.Walk), Role("run", clips.Run), Role("turnL", clips.TurnLeft), Role("turnR", clips.TurnRight), Role("sit", clips.Sit), Role("sleep", clips.Sleep), Role("use", clips.Use))
            + $" missing=[{string.Join(',', missing)}] source=[{clips.Source ?? "none recorded"}]");
    }

    public static int DespawnAll(Node cellRoot)
    {
        if (cellRoot.GetNodeOrNull(PuppetsName) is not { } puppets) return 0;
        CompanionCarry.Rescue(puppets, false);
        int count = puppets.GetChildCount();
        cellRoot.RemoveChild(puppets);
        puppets.QueueFree();
        if (cellRoot.GetNodeOrNull(NpcCrowdBudget.NodeName) is { } budget)
        {
            cellRoot.RemoveChild(budget);
            budget.QueueFree();
        }
        return count;
    }

    public static Node3D? Spawn(Node3D marker, Node puppetParent, bool deferBakes = false)
    {
        _failedKey = null;
        try
        {
            bool enabled = ActorEnableState.IsEnabled(marker);
            Applied.AddOrUpdate(marker, new StrongBox<bool>(enabled));
            if (!enabled || CompanionCarry.SkipsMarker(marker.GetMeta(RefKeyMeta, "").AsString())) return null;
            var puppet = SpawnCore(marker, puppetParent, deferBakes);
            if (puppet != null)
            {
                Spawned.AddOrUpdate(marker, puppet);
                SpawnRetries.Reset(marker.GetMeta(BaseKeyMeta, "").AsString());
                GameSaveService.RestoreActor(puppet);
            }
            else if (_failedKey != null)
            {
                EngineLiveState.Readiness.Fail(Subsystem.Actors, new InvalidOperationException($"{marker.Name}: no puppet, the rig for {_failedKey} could not be built"));
                RetrySpawn(marker, _failedKey);
            }
            return puppet;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcSpawn] {marker.Name}: {ex}");
            EngineLiveState.Readiness.Fail(Subsystem.Actors, ex);
            RetrySpawn(marker, null);
            return null;
        }
    }

    private static void RetrySpawn(Node3D marker, string? failedKey)
    {
        if (!GodotObject.IsInstanceValid(marker) || !marker.IsInsideTree()) return;
        string key = marker.GetMeta(BaseKeyMeta, "").AsString();
        if (!SpawnRetries.TryTake(key))
        {
            GD.PrintErr($"[NpcSpawn] {marker.Name}: spawn failed {SpawnRetries.Max + 1} times; giving up (see the error above).");
            return;
        }
        if (failedKey != null) Failed.Remove(failedKey);
        GD.Print($"[NpcSpawn] {marker.Name}: spawn failed; retry {SpawnRetries.Used(key)} of {SpawnRetries.Max} queued.");
        Enqueue(marker);
    }

    public static int Sync(Node3D marker)
    {
        bool enabled = ActorEnableState.IsEnabled(marker);
        if (!Applied.TryGetValue(marker, out var applied) || applied.Value == enabled) return 0;
        if (enabled) return PuppetsFor(marker) is { } puppets && Spawn(marker, puppets, true) != null ? 1 : 0;
        applied.Value = false;
        if (PuppetOf(marker) is not { } puppet) return 0;
        Despawn(puppet);
        return 1;
    }

    internal static void PumpBakes()
    {
        while (PendingBakes.TryDequeue(out var marker))
        {
            Queued.Remove(marker);
            if (!GodotObject.IsInstanceValid(marker) || !marker.IsInsideTree() || PuppetOf(marker) != null
                || PuppetsFor(marker) is not { } puppets) continue;
            Spawn(marker, puppets);
            break;
        }
        if (PendingBakes.Count == 0 && _pumping && Engine.GetMainLoop() is SceneTree tree)
        {
            tree.ProcessFrame -= PumpBakes;
            _pumping = false;
        }
    }

    private static void Enqueue(Node3D marker)
    {
        if (Queued.TryGetValue(marker, out _)) return;
        Queued.Add(marker, new object());
        PendingBakes.Enqueue(marker);
        if (!_pumping && Engine.GetMainLoop() is SceneTree tree)
        {
            tree.ProcessFrame += PumpBakes;
            _pumping = true;
        }
    }

    internal static bool NeedsBake(string npcKey, ulong seed) =>
        !_unavailable && !Failed.Contains(npcKey) && GameLoader.TryParseKey(npcKey, out var key)
        && Path.GetFileName(key.Plugin) == key.Plugin && !ResourceLoader.Exists(ScenePath(key, seed))
        && (NpcRigSharing.IsDependent(npcKey) || !ResourceLoader.Exists(NpcRigSharing.SharedPath(key)));

    public static Node3D? PuppetOf(Node3D marker) =>
        Spawned.TryGetValue(marker, out var puppet) && GodotObject.IsInstanceValid(puppet) && puppet.IsInsideTree() ? puppet : null;

    public static Node? PuppetsFor(Node3D marker)
    {
        for (var node = marker.GetParent(); node != null; node = node.GetParent())
            if (node.GetNodeOrNull(PuppetsName) is { } puppets) return puppets;
        return null;
    }

    public static void Despawn(Node3D puppet)
    {
        puppet.GetParent()?.RemoveChild(puppet);
        puppet.QueueFree();
    }

    private static void Collect(Node node, List<Node3D> markers)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Node3D marker && marker.IsInGroup(MarkerGroup)) markers.Add(marker);
            Collect(child, markers);
        }
    }

    private static Node3D? SpawnCore(Node3D marker, Node puppetParent, bool deferBakes)
    {
        string baseType = marker.GetMeta(BaseTypeMeta, "").AsString();
        string baseKey = marker.GetMeta(BaseKeyMeta, "").AsString();
        string refKey = marker.GetMeta(RefKeyMeta, marker.GetPath().ToString()).AsString();
        if (ChoiceOf(baseType, baseKey, refKey) is not { } choice)
        {
            GD.Print($"[NpcSpawn] {marker.Name}: leveled list {baseKey} gave no NPC_ at level {SpawnLevel}; no puppet.");
            return null;
        }
        string npcKey = choice.NpcKey;
        baseType = choice.BaseType;
        if (baseType != "Npc")
        {
            GD.Print($"[NpcSpawn] {marker.Name}: stub, base type '{baseType}' ({baseKey}) is not a plain NPC_; no puppet.");
            return null;
        }
        if (!marker.IsInsideTree() || !puppetParent.IsInsideTree())
        {
            GD.PrintErr($"[NpcSpawn] {marker.Name}: the marker and the puppet parent must both be in the tree; no puppet.");
            return null;
        }

        ulong seed = SeedFor(refKey);
        if (deferBakes && NeedsBake(npcKey, seed))
        {
            Enqueue(marker);
            return null;
        }
        var puppet = Build(npcKey, baseKey, refKey, seed, marker.Name, puppetParent, marker.GlobalTransform, false);
        puppet?.SetMeta(MarkerMeta, marker.GetPath());
        return puppet;
    }

    internal static Node3D? Build(string npcKey, string baseKey, string refKey, ulong seed, string name, Node puppetParent,
                                  Transform3D at, bool dynamic)
    {
        if (!GameLoader.TryParseKey(npcKey, out var key) || Path.GetFileName(key.Plugin) != key.Plugin)
        {
            GD.PrintErr($"[NpcSpawn] {name}: base form key '{npcKey}' does not parse; no puppet.");
            return null;
        }
        var level = LevelOf(npcKey, key);
        if (NpcRigSharing.IsDependent(npcKey)) NpcRigSharing.RetireShared(key);
        var packed = Load(NpcRigSharing.SharedPath(key)) ?? Load(ScenePath(key, seed));
        if (packed == null && (dynamic || !_unavailable && !Failed.Contains(npcKey)))
        {
            if (Bake(key, npcKey, seed, level.Level) is { } baked) packed = Load(baked);
            else
            {
                Failed.Add(npcKey);
                _failedKey = npcKey;
            }
        }
        if (packed?.Instantiate<Node3D>() is not { } puppet) return null;

        puppet.Name = name;
        puppet.SetMeta(BaseKeyMeta, baseKey);
        puppet.SetMeta(RefKeyMeta, refKey);
        if (npcKey != baseKey) puppet.SetMeta(ResolvedMeta, npcKey);
        if (dynamic) puppet.SetMeta(DynamicMeta, true);
        puppetParent.AddChild(puppet, true);
        puppet.GlobalTransform = at;
        NpcPickBody.Attach(puppet);
        if (GameLoader.HasLoadOrder) NpcDialogue.Warm(GameLoader.Records);
        var identity = ActorRegistry.Register(puppet, ActorKind.Npc);
        if (identity != null)
        {
            if (level.Unresolved is { } why && LevelStubbed.Add(npcKey)) GD.Print($"[NpcSpawn] {name} ({npcKey}): stub, level {level.Level} stands in; {why}.");
            var state = StateFor(npcKey, key, level);
            if (state.Health == null && LevelStubbed.Add("health:" + npcKey)) GD.Print($"[NpcSpawn] {name} ({npcKey}): stub, health is unset and the NPC_ cannot be damaged; its calculated health or its Essential/Protected/Invulnerable flags are not proven (templated, level multiplier, unproven level, no DNAM or an unread ACBS).");
            var combat = ActorCombat.Attach(puppet, identity, state);
            ActorDetection.Attach(puppet, identity, combat);
            ActorPerception.Attach(puppet, identity, combat);
        }
        foreach (var stub in puppet.GetMeta(StubsMeta, "").AsString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            GD.Print($"[NpcSpawn] {name} ({npcKey}): stub, {stub}");
        var clips = NpcClipSet.Parse(puppet.GetMeta(IdleMeta, "").AsString(), puppet.GetMeta(ClipsMeta, "").AsString());
        string skeleton = puppet.GetMeta(SkeletonMeta, "").AsString();
        NpcIdle.Retry(clips, skeleton);
        NpcAnimationEvents.Ensure(puppet);
        ActorLighting.Apply(puppet, true);
        bool locomotion = NpcLocomotion.Attach(puppet, clips) != null;
        bool idlePlayer = !locomotion && NpcIdle.Attach(puppet, clips.Idle) != null;
        TraceRig(puppet, npcKey, clips, locomotion, idlePlayer, skeleton);
        if (identity != null)
        {
            var profile = NavProfileOf(npcKey, key, level.Level, seed);
            PackageRunner.Attach(puppet, identity, RadiclePackageSource.Instance, profile);
            CompanionFollower.Attach(puppet, identity, profile);
            NpcCombatDriver.Attach(puppet, identity, profile);
        }
        return puppet;
    }

    public static bool ReevaluatePackage(string refKey)
    {
        if (ActorRegistry.Find(refKey)?.Node.GetNodeOrNull<PackageRunner>(PackageRunner.NodeName) is not { } runner) return false;
        runner.Reevaluate();
        return true;
    }

    internal static ActorState StateFor(string npcKey, RadicleKey key, ActorLevelResult level)
    {
        float? health = level.Unresolved == null ? HealthOf(npcKey, key) : null;
        return ActorSpawnRules.StateFor(level.Level, level.Unresolved, health, health == null ? null : ProtectionOf(npcKey, key));
    }

    private static ActorProtection? ProtectionOf(string npcKey, RadicleKey key)
    {
        if (ProtectionCache.TryGetValue(npcKey, out var cached)) return cached;
        if (ProtectionRead(key) is not { } protection) return null;
        ProtectionCache[npcKey] = protection;
        return protection;
    }

    private static float? HealthOf(string npcKey, RadicleKey key)
    {
        if (HealthCache.TryGetValue(npcKey, out var cached)) return cached;
        if (ActorSpawnRules.ProvenHealth(HealthRead(key)) is not { } health) return null;
        HealthCache[npcKey] = health;
        return health;
    }

    internal static ActorLevelResult LevelOf(string npcKey, RadicleKey key)
    {
        if (LevelCache.TryGetValue(npcKey, out var level)) return level;
        level = LevelRead(key);
        if (level.Unresolved == null) LevelCache[npcKey] = level;
        return level;
    }

    // Requires: main thread; the pin lives per placed ref key for the load session so a level change never re-chooses, and a failed pick is never pinned.
    private static SpawnChoice? ChoiceOf(string baseType, string baseKey, string refKey)
    {
        if (_pinSession != GameLoader.SessionId)
        {
            Pins.Clear();
            _pinSession = GameLoader.SessionId;
        }
        Pins.TryGetValue(refKey, out var pinned);
        var pick = LeveledPick;
        var choice = SpawnIdentity.Choose(baseType, baseKey, pinned, pick is null ? null : () => pick(baseKey, SpawnLevel, SeedFor(refKey)));
        if (choice is { Leveled: true } leveled) Pins[refKey] = leveled.NpcKey;
        return choice;
    }

    // Requires: main thread, and for the streamer this runs before PapyrusRuntime.AttachCell so the merged fo4_scripts metadata is seen by the attach.
    public static void ResolveLeveled(Node cellRoot)
    {
        var markers = new List<Node3D>();
        Collect(cellRoot, markers);
        ResolveLeveled(markers);
    }

    private static void ResolveLeveled(List<Node3D> markers)
    {
        foreach (var marker in markers)
        {
            if (marker.GetMeta(BaseTypeMeta, "").AsString() != SpawnIdentity.LeveledType) continue;
            try
            {
                string baseKey = marker.GetMeta(BaseKeyMeta, "").AsString();
                string refKey = marker.GetMeta(RefKeyMeta, marker.GetPath().ToString()).AsString();
                if (ChoiceOf(SpawnIdentity.LeveledType, baseKey, refKey) is { Leveled: true } choice) AttachChosenScripts(marker, choice.NpcKey);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                GD.PrintErr($"[NpcSpawn] {marker.Name}: the chosen NPC_ scripts could not be resolved: {ex.Message}");
            }
        }
    }

    // Requires: main thread; the scripts stay on the marker (one owner, one attach) and the ref's own scripts win by name.
    private static void AttachChosenScripts(Node3D marker, string npcKey)
    {
        if (marker.GetMeta(ChosenScriptsMeta, "").AsString() == npcKey) return;
        if (!GameLoader.HasLoadOrder || !GameLoader.TryParseKey(npcKey, out var key)) return;
        BaseScripts.Result read;
        GameLoader.EnvLifetime.EnterReadLock();
        try
        {
            var records = GameLoader.Records;
            read = records.Winner(key) is { } record ? BaseScripts.Read(records.Environment, record) : new BaseScripts.Result(null, false);
        }
        finally { GameLoader.EnvLifetime.ExitReadLock(); }
        IReadOnlyList<ScriptAttachment> chosen = read.Scripts is null ? Array.Empty<ScriptAttachment>() : ScriptAttachments.Parse(JsonConvert.SerializeObject(PlacedRefJson.Scripts(read.Scripts)));
        string? own = marker.HasMeta(ScriptAttachments.MetadataKey) ? marker.GetMeta(ScriptAttachments.MetadataKey).AsString() : null;
        if (SpawnIdentity.MergeScripts(own, chosen) is { } merged && merged != own)
        {
            marker.SetMeta(ScriptAttachments.MetadataKey, merged);
            GD.Print($"[NpcSpawn] {marker.Name}: attached the scripts of {npcKey} ({chosen.Count} from the NPC_) to the placed reference.");
        }
        if (read.Incomplete) marker.SetMeta("fo4_script_source_unsupported", true);
        marker.SetMeta(ChosenScriptsMeta, npcKey);
    }

    internal static string? PickLeveledNpc(string listKey, ushort level, ulong seed)
    {
        if (!GameLoader.TryParseKey(listKey, out var key) || LeveledPickRead(key, level, seed) is not { } pick) return null;
        return pick.Reason == Radicle.Interop.LeveledPickReason.Picked && pick.Entries.Count > 0 ? pick.Entries[0].ToString() : null;
    }

    private static Radicle.Interop.LeveledPickOutcome? ReadLeveledPick(RadicleKey list, ushort level, ulong seed)
    {
        try { return GameLoader.Records.Environment.LeveledPick(list, level, seed); }
        catch (Exception e)
        {
            if (LevelStubbed.Add("pick:" + list)) GD.Print($"[NpcSpawn] {list}: stub, no leveled pick ({e.Message}).");
            return null;
        }
    }

    internal static NavProfile? NavProfileOf(string npcKey, RadicleKey key, ushort level, ulong seed)
    {
        if (ProfileCache.TryGetValue(npcKey, out var cached)) return cached;
        var profile = ProfileRead(key, level, seed);
        if (profile is { } resolved) ProfileCache[npcKey] = resolved;
        return profile;
    }

    // Constraint: null means keep NavProfile.Biped; the race comes from Radicle's visual plan, never from vanilla data.
    private static NavProfile? ReadProfile(RadicleKey key, ushort level, ulong seed)
    {
        try
        {
            var env = GameLoader.Records.Environment;
            return env.Winner(key) is { } record ? RadicleNavWalk.Profile(env, env.NpcVisual(record, level, seed).Race) : null;
        }
        catch (Exception e)
        {
            if (LevelStubbed.Add(key.ToString())) GD.Print($"[NpcSpawn] {key}: nav profile read failed, biped stands in; {e.Message}");
            return null;
        }
    }

    private static PackedScene? Load(string path) => ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;

    private static string? Unavailable(string reason)
    {
        if (!_unavailable) GD.Print($"[NpcSpawn] cannot build NPC rigs: {reason}. Markers that have no baked scene stay inert.");
        _unavailable = true;
        return null;
    }

    private static string? Bake(RadicleKey key, string npcKey, ulong seed, ushort level)
    {
        var fo4 = ToolPaths.Fallout4Root();
        if (fo4 == null) return Unavailable("the Fallout 4 install was not found");
        string dataDir = Path.Combine(fo4, "Data");

        Radicle.Interop.NpcVisual visual;
        string scenePath;
        try
        {
            var env = GameLoader.Records.Environment;
            if (env.Winner(key) is not { } record)
            {
                GD.PrintErr($"[NpcSpawn] {key}: no winning NPC_ record in the load order; no puppet.");
                return null;
            }
            visual = env.NpcVisual(record, level, seed);
            bool shared = NpcRigSharing.IsShared(npcKey, visual, probe => env.NpcVisual(record, level, probe));
            scenePath = shared ? NpcRigSharing.SharedPath(key) : ScenePath(key, seed);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[NpcSpawn] {key}: {ex.GetType().Name}: {ex.Message}; no puppet.");
            return null;
        }

        if (visual.SkeletonHkxEntry is not { } skeletonEntry || !ActorRigAssembly.IsSafeEntry(skeletonEntry))
        {
            GD.Print($"[NpcSpawn] {key}: stub, Radicle returned no usable skeleton for race {visual.RaceEditorId ?? "?"}; no puppet.");
            foreach (var stub in visual.Stubs) GD.Print($"[NpcSpawn] {key}: stub, {stub}");
            return null;
        }

        string temp = Path.Combine(Path.GetTempPath(), "OCNpcRig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            GameLoader.EnsureAssetRoots(dataDir);
            var entries = new List<string>();
            foreach (var part in visual.Parts)
                if (ActorRigAssembly.IsSafeEntry(part.MeshEntry)) entries.Add(part.MeshEntry);
                else GD.PrintErr($"[NpcSpawn] {key}: mesh entry '{part.MeshEntry}' is not a relative archive path; skipped.");
            if (entries.Count == 0)
            {
                GD.Print($"[NpcSpawn] {key}: stub, no body parts from Radicle for race {visual.RaceEditorId ?? "?"}; no puppet.");
                foreach (var stub in visual.Stubs) GD.Print($"[NpcSpawn] {key}: stub, {stub}");
                return null;
            }
            var tint = visual.SkinTint is { Length: >= 3 } skinTint ? skinTint : null;
            var rig = ActorRigAssembly.Build($"Npc_{key.LocalId:X8}", skeletonEntry, ActorRigAssembly.SkeletonScenePath(skeletonEntry),
                entries, tint, dataDir, temp, TextureService.ResolveNif);
            if (rig == null) { GD.PrintErr($"[NpcSpawn] {key}: no skinned meshes came out of {entries.Count} part(s); no puppet."); return null; }

            var clips = NpcIdle.ResolveClips(NpcClipSet.FromVisual(visual), skeletonEntry, dataDir, temp, visual.Female);
            NpcIdle.Ensure(clips, skeletonEntry, dataDir, temp);
            rig.Root.SetMeta(IdleMeta, clips.Idle ?? "");
            rig.Root.SetMeta(ClipsMeta, clips.Serialize());
            rig.Root.SetMeta(SkeletonMeta, skeletonEntry);
            rig.Root.SetMeta(StubsMeta, string.Join('\n', visual.Stubs));
            var scene = new PackedScene();
            var err = scene.Pack(rig.Root);
            rig.Root.Free();
            if (err != Error.Ok) { GD.PrintErr($"[NpcSpawn] {key}: pack failed: {err}"); return null; }
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(scenePath.GetBaseDir()));
            err = ResourceSaver.Save(scene, scenePath);
            if (err != Error.Ok) { GD.PrintErr($"[NpcSpawn] {key}: save failed: {err}"); return null; }
            if (scenePath == NpcRigSharing.SharedPath(key)) NpcRigSharing.RetireSeeded(key);
            else NpcRigSharing.RetireShared(key);
            GD.Print($"[NpcSpawn] Baked {scenePath} ({rig.Skinned} skinned meshes, race {visual.RaceEditorId ?? "?"}, female {visual.Female}).");
            return scenePath;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }
}
