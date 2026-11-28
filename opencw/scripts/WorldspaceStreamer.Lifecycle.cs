using Godot;
using System.Collections.Generic;
using System.Runtime.Loader;
using OpenCommonwealth.Services.Terrain;
using OpenCommonwealth.Services.Worldspace;
using OpenCommonwealth.Services.Ui.Streaming;

namespace OpenCommonwealth;

public partial class WorldspaceStreamer
{
    internal System.Func<string, ResourceLoader.ThreadLoadStatus>? LoadStatusOverride;
    internal System.Action<string>? DisposeFetchedOverride;
    internal System.Func<string, ResourceLoader.CacheMode, PackedScene?>? SyncLoadOverride;
    // ----- desired-set recompute + eviction marking -----

    private void RecomputeDesired(Vector2I cell)
    {
        if (_manifest == null) return;

        // Full-detail ring: manifest cells with a compiled scene within Chebyshev radius.
        int radius = Radius;
        int bs = BucketSize();
        var cells = StreamRing.CellPlan(_manifest, cell, radius, _cellSlots.Keys);
        var buckets = StreamRing.BucketPlan(_manifest, cell, radius, bs, _persSlots.Keys);
        foreach (var key in cells.Keep) Refresh(_cellSlots, key);
        foreach (var key in buckets.Keep) Refresh(_persSlots, key);
        foreach (var load in cells.Queue) EnsureQueued(_cellSlots, load.Key, load.ResPath, false, load.Dist);
        foreach (var load in buckets.Queue) EnsureQueued(_persSlots, load.Key, load.ResPath, true, load.Dist);

        MarkEvictions(_cellSlots, cell, false, bs);
        MarkEvictions(_persSlots, cell, true, bs);
    }

    private void Refresh(Dictionary<Vector2I, Slot> slots, Vector2I key)
    {
        if (!slots.TryGetValue(key, out var slot)) return;
        slot.EvictMarked = false;
        slot.Epoch = _epoch;
    }

    private void EnsureQueued(Dictionary<Vector2I, Slot> slots, Vector2I key, string resPath, bool persistent, int dist)
    {
        if (slots.ContainsKey(key)) return;

        var slot = new Slot
        {
            State = SlotState.Queued,
            Epoch = _epoch,
            ResPath = resPath,
            IsPersistent = persistent,
            Key = key,
        };
        slots[key] = slot;
        _loadQueue.Enqueue(slot, dist);
    }

    private void MarkEvictions(Dictionary<Vector2I, Slot> slots, Vector2I cell, bool persistent, int bs)
    {
        foreach (var slot in slots.Values)
        {
            if (slot.State != SlotState.Resident) continue; // Queued/Loading handled at pop/integrate.
            if (StreamRing.Evicts(slot.Key, cell, persistent, bs, Radius, EvictHysteresis))
            {
                if (!slot.EvictMarked) { slot.EvictMarked = true; _evictQueue.Enqueue(slot); }
            }
            else
            {
                slot.EvictMarked = false;
            }
        }
    }

    // ----- load pump -----

    private static void Drain(List<string> paths, bool wait)
    {
        for (int i = paths.Count - 1; i >= 0; i--)
        {
            var path = paths[i];
            var status = ResourceLoader.LoadThreadedGetStatus(path);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress && !wait) continue;
            if (status != ResourceLoader.ThreadLoadStatus.InvalidResource) DisposeFetched(path);
            paths.RemoveAt(i);
        }
    }

    private static bool RetrievedRecently(string path)
    {
        if (!_retrievedFrame.TryGetValue(path, out var frame)) return false;
        if (Engine.GetProcessFrames() - frame <= 1) return true;
        _retrievedFrame.Remove(path);
        return false;
    }

    private static void DisposeFetched(string path)
    {
        _retrievedFrame[path] = Engine.GetProcessFrames();
        var res = ResourceLoader.LoadThreadedGet(path);
        if (res != null && !SceneHolders.ContainsKey(res)) res.Dispose();
    }

    private static void SubscribeOrphanPump()
    {
        if (!_orphanUnloadHooked && AssemblyLoadContext.GetLoadContext(typeof(WorldspaceStreamer).Assembly) is { IsCollectible: true } context)
            HookOrphanUnload(context);
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var pump = Callable.From(DrainOrphansOnFrame);
        if (!tree.IsConnected(SceneTree.SignalName.ProcessFrame, pump)) tree.Connect(SceneTree.SignalName.ProcessFrame, pump);
    }

    internal static void HookOrphanUnload(AssemblyLoadContext context)
    {
        _orphanUnloadHooked = true;
        context.Unloading += _ => DrainOrphansForUnload();
    }

    private static void UnsubscribeOrphanPump()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var pump = Callable.From(DrainOrphansOnFrame);
        if (tree.IsConnected(SceneTree.SignalName.ProcessFrame, pump)) tree.Disconnect(SceneTree.SignalName.ProcessFrame, pump);
    }

    internal static bool OrphanPumpConnected => Engine.GetMainLoop() is SceneTree tree && tree.IsConnected(SceneTree.SignalName.ProcessFrame, Callable.From(DrainOrphansOnFrame));

    private static void DrainOrphansOnFrame()
    {
        Drain(_orphanLoads, wait: false);
        if (_orphanLoads.Count == 0) UnsubscribeOrphanPump();
    }

    private static void DrainOrphansForUnload()
    {
        Drain(_orphanLoads, wait: true);
        UnsubscribeOrphanPump();
    }

    private void HandOffInflight()
    {
        _inTree.Remove(this);
        foreach (var s in _inflight) _abandoned.Add(s.ResPath);
        var inflight = _inflight.ToArray();
        _inflight.Clear();
        try
        {
            while (_readyQueue.Count > 0) DiscardSlot(_readyQueue.Dequeue());
            foreach (var s in inflight) DiscardSlot(s);
        }
        finally
        {
            HandOffAbandoned();
        }
    }

    private void HandOffAbandoned()
    {
        Drain(_abandoned, wait: false);
        _orphanLoads.AddRange(_abandoned);
        _abandoned.Clear();
        if (_orphanLoads.Count > 0) SubscribeOrphanPump();
    }

    private static bool HeldByStalledStreamer(string path)
    {
        ulong frame = Engine.GetProcessFrames();
        foreach (var owner in _inTree)
            if (frame - owner._pumpFrame > PumpStallFrames && owner._inflight.Exists(s => s.ResPath == path)) return true;
        return false;
    }

    private void PumpLoads()
    {
        _pumpFrame = Engine.GetProcessFrames();
        Drain(_orphanLoads, wait: false);
        int fallbackCount = 0;

        // Poll in-flight requests first so finished ones free their slot for the fill below.
        for (int i = _inflight.Count - 1; i >= 0; i--)
        {
            var slot = _inflight[i];
            var status = LoadStatusOverride?.Invoke(slot.ResPath) ?? ResourceLoader.LoadThreadedGetStatus(slot.ResPath);
            switch (status)
            {
                case ResourceLoader.ThreadLoadStatus.InProgress:
                    break;
                case ResourceLoader.ThreadLoadStatus.Loaded:
                    _inflight.RemoveAt(i);
                    _retrievedFrame[slot.ResPath] = Engine.GetProcessFrames();
                    var res = ResourceLoader.LoadThreadedGet(slot.ResPath) as PackedScene;
                    if (res == null) { NoteCellLoadFailure(slot); DiscardSlot(slot); break; }
                    HoldScene(slot, res);
                    _readyQueue.Enqueue(slot);
                    break;
                case ResourceLoader.ThreadLoadStatus.InvalidResource:
                    _inflight.RemoveAt(i);
                    if (!slot.Requeued)
                    {
                        slot.Requeued = true;
                        slot.State = SlotState.Queued;
                        _loadQueue.Enqueue(slot, Distance(slot));
                        break;
                    }
                    GD.PrintErr($"WorldspaceStreamer: failed to load {slot.ResPath} (status {status}).");
                    NoteCellLoadFailure(slot);
                    DiscardSlot(slot);
                    break;
                default:
                    _inflight.RemoveAt(i);
                    if (DisposeFetchedOverride != null) DisposeFetchedOverride(slot.ResPath);
                    else DisposeFetched(slot.ResPath);
                    if (!FallbackBudget.AttemptFailedThreadedSync(SynchronousFallback)
                        || slot.EvictMarked
                        || slot.Epoch != _epoch
                        || !StreamRing.Wanted(Distance(slot), Radius))
                    {
                        GD.PrintErr($"WorldspaceStreamer: failed to load {slot.ResPath} (status {status}).");
                        NoteCellLoadFailure(slot);
                        DiscardSlot(slot);
                        break;
                    }
                    slot.PendingFallback = true;
                    slot.FallbackStatus = status;
                    slot.State = SlotState.Queued;
                    _loadQueue.Enqueue(slot, Distance(slot));
                    break;
            }
        }

        // Fill up to MaxInflight from the priority queue (closest ring distance first).
        var cacheMode = _flushReplaceOnce ? ResourceLoader.CacheMode.Replace : ResourceLoader.CacheMode.Reuse;
        List<(Slot Slot, int Priority)>? held = null;
        while (_inflight.Count < MaxInflight && _loadQueue.TryDequeue(out var slot, out var priority))
        {
            if (slot.State != SlotState.Queued) continue;
            if (!StreamRing.Wanted(Distance(slot), Radius)) { DiscardSlot(slot); continue; }
            if (slot.PendingFallback)
            {
                if (slot.EvictMarked) { DiscardSlot(slot); continue; }
                if (!FallbackBudget.AttemptFailedThreadedSync(SynchronousFallback))
                {
                    GD.PrintErr($"WorldspaceStreamer: failed to load {slot.ResPath} and synchronous fallback is disabled.");
                    NoteCellLoadFailure(slot);
                    DiscardSlot(slot);
                    continue;
                }
                var slots = slot.IsPersistent ? _persSlots : _cellSlots;
                bool current = slots.TryGetValue(slot.Key, out var currentSlot) && ReferenceEquals(currentSlot, slot);
                var decision = FallbackBudget.Decide(current, slot.Epoch == _epoch, StreamRing.Wanted(Distance(slot), Radius), fallbackCount);
                if (decision == FallbackDecision.Discard) { DiscardSlot(slot); continue; }
                if (decision == FallbackDecision.Defer)
                {
                    (held ??= new List<(Slot Slot, int Priority)>()).Add((slot, priority));
                    continue;
                }
                fallbackCount++;
                slot.PendingFallback = false;
                var syncScene = SyncLoadOverride != null
                    ? SyncLoadOverride(slot.ResPath, slot.CacheMode)
                    : ResourceLoader.Load<PackedScene>(slot.ResPath, "PackedScene", slot.CacheMode);
                if (syncScene != null)
                {
                    var threaded = slot.FallbackStatus.HasValue ? $" (status {slot.FallbackStatus.Value})" : "";
                    GD.PushWarning($"WorldspaceStreamer: threaded load failed for {slot.ResPath}{threaded}; synchronous fallback succeeded (cache {slot.CacheMode}).");
                    slot.FallbackStatus = null;
                    slot.State = SlotState.Loading;
                    HoldScene(slot, syncScene);
                    _readyQueue.Enqueue(slot);
                    continue;
                }
                GD.PrintErr($"WorldspaceStreamer: synchronous fallback failed for {slot.ResPath}.");
                NoteCellLoadFailure(slot);
                DiscardSlot(slot);
                continue;
            }
            if ((ResourceLoader.LoadThreadedGetStatus(slot.ResPath) != ResourceLoader.ThreadLoadStatus.InvalidResource && !(cacheMode == ResourceLoader.CacheMode.Reuse && HeldByStalledStreamer(slot.ResPath)))
                || (cacheMode == ResourceLoader.CacheMode.Replace && RetrievedRecently(slot.ResPath)))
            {
                (held ??= new List<(Slot Slot, int Priority)>()).Add((slot, priority));
                continue;
            }

            var err = ResourceLoader.LoadThreadedRequest(slot.ResPath, "PackedScene", useSubThreads: false, cacheMode);
            if (err != Error.Ok)
            {
                if (SynchronousFallback && !slot.EvictMarked)
                {
                    var slots = slot.IsPersistent ? _persSlots : _cellSlots;
                    bool current = slots.TryGetValue(slot.Key, out var currentSlot) && ReferenceEquals(currentSlot, slot);
                    var decision = FallbackBudget.Decide(current, slot.Epoch == _epoch, StreamRing.Wanted(Distance(slot), Radius), fallbackCount);
                    if (decision == FallbackDecision.Defer)
                    {
                        slot.PendingFallback = true;
                        slot.CacheMode = cacheMode;
                        (held ??= new List<(Slot Slot, int Priority)>()).Add((slot, priority));
                        continue;
                    }
                    if (decision == FallbackDecision.Discard)
                    {
                        DiscardSlot(slot);
                        continue;
                    }
                    fallbackCount++;
                    var res = ResourceLoader.Load<PackedScene>(slot.ResPath, "PackedScene", cacheMode);
                    if (res != null)
                    {
                        GD.PushWarning($"WorldspaceStreamer: LoadThreadedRequest failed for {slot.ResPath}; synchronous fallback succeeded (cache {cacheMode}).");
                        slot.State = SlotState.Loading;
                        HoldScene(slot, res);
                        _readyQueue.Enqueue(slot);
                        continue;
                    }
                }
                GD.PrintErr($"WorldspaceStreamer: LoadThreadedRequest failed for {slot.ResPath}: {err}.");
                NoteCellLoadFailure(slot);
                DiscardSlot(slot);
                continue;
            }

            slot.State = SlotState.Loading;
            slot.CacheMode = cacheMode;
            _inflight.Add(slot);
        }

        if (held != null)
            foreach (var (heldSlot, heldPriority) in held) _loadQueue.Enqueue(heldSlot, heldPriority);

        // Keep CacheMode.Replace active for the whole post-flush drain, then revert to Reuse.
        if (_flushReplaceOnce && _loadQueue.Count == 0 && _orphanLoads.Count == 0)
            _flushReplaceOnce = false;
    }

    // ----- integrate + evict -----

    private void IntegrateAndEvict()
    {
        int budgetCells = LoadingMode ? 16 : CellsPerFrame;
        double budgetMs = LoadingMode ? 100.0
            : Services.FarShadow.FarShadowFrameShare.Shared.IntegrateMs((long)Engine.GetProcessFrames(), IntegrateBudgetMs);

        _sw.Restart();
        int integrated = 0;
        while (_readyQueue.Count > 0 && integrated < budgetCells && _sw.Elapsed.TotalMilliseconds < budgetMs)
        {
            var slot = _readyQueue.Dequeue();
            if (slot.State != SlotState.Loading || slot.Loaded == null) continue;

            // Drop results that are stale (superseded epoch) or no longer within the ring.
            if (slot.Epoch != _epoch || !StreamRing.Wanted(Distance(slot), Radius))
            {
                DiscardSlot(slot);
                continue;
            }

            // Guard against a malformed artifact whose root is not Node3D (compiler emits Node3D roots).
            Node? instance;
            try { instance = InstantiateSlot(slot); }
            catch
            {
                DiscardSlot(slot);
                throw;
            }
            if (instance is not Node3D inst)
            {
                GD.PrintErr($"WorldspaceStreamer: {slot.ResPath} root is not a Node3D; skipping.");
                NoteCellLoadFailure(slot);
                instance?.Free();
                DiscardSlot(slot);
                continue;
            }
            inst.Position = slot.IsPersistent ? BucketOrigin(slot.Key) : WorldGrid.CellOrigin(slot.Key);
            if (IgnoreClutterFade) ClearVisibilityRanges(inst);
            Lighting.EnvironmentController.Active?.AdoptCell(inst);
            Services.Water.ShoreSolids.Shelter(inst);
            AddChild(inst);         // NEVER SetOwner: streamed nodes stay out of the saved scene.
            HideBaseGameWater(inst);
            Services.Water.ShoreSolids.Split(inst);
            Services.Physics.PlayerSolids.Apply(inst);
            ReferenceEnable.Register(inst);
            slot.Root = inst;
            slot.TerrainMesh = slot.IsPersistent ? null : inst.GetNodeOrNull<MeshInstance3D>("Terrain");
            slot.TerrainBody = slot.IsPersistent ? null : inst.GetNodeOrNull<StaticBody3D>("TerrainCollision");
            using (var terrainMesh = slot.TerrainMesh?.Mesh) slot.HasTerrain = terrainMesh != null;
            slot.State = SlotState.Resident;
            _failedCellLoads.Remove(slot.ResPath);
            if (SpawnActors && !Engine.IsEditorHint()) NpcPuppetSpawner.ResolveLeveled(inst);
            PapyrusRuntime.Find(this)?.AttachCell(slot.ResPath, inst);
            if (!slot.IsPersistent) NavService.AttachCell(inst, slot.ResPath);
            OnSlotIntegrated(slot);
            SpawnActorsIn(inst);
            if (!Engine.IsEditorHint()) GameSaveService.RestoreCell(inst);
            ReleaseScene(slot);
            integrated++;
        }
        _lastIntegrateMs = _sw.Elapsed.TotalMilliseconds;

        // Evict a bounded number of far cells per frame (QueueFree of a big cell hitches).
        int evicted = 0;
        while (_evictQueue.Count > 0 && evicted < EvictionsPerFrame)
        {
            var slot = _evictQueue.Dequeue();
            if (!slot.EvictMarked) continue;   // came back into range before we got to it
            slot.EvictMarked = false;
            RetireSlot(slot);
            evicted++;
        }
    }

    // ----- helpers -----

    private static void ClearVisibilityRanges(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is GeometryInstance3D g)
            {
                g.VisibilityRangeEnd = 0;
                g.VisibilityRangeEndMargin = 0;
            }
            foreach (var child in node.GetChildren()) stack.Push(child);
        }
    }

    private int BucketSize() => StreamRing.BucketSize(_manifest?.PersistentBucketSize ?? 0);

    private int Distance(Slot slot)
    {
        if (!_hasLastCell) return 0;
        return slot.IsPersistent
            ? BucketDistance(slot.Key, _lastCell, BucketSize())
            : WorldGrid.Chebyshev(slot.Key, _lastCell);
    }

    private static int BucketDistance(Vector2I bucket, Vector2I cell, int bs) => StreamRing.BucketDistance(bucket, cell, bs);

    private Vector3 BucketOrigin(Vector2I bucket)
    {
        int bs = BucketSize();
        return WorldGrid.CellOrigin(new Vector2I(bucket.X * bs, bucket.Y * bs));
    }

    /// <summary>Removes a slot from its dict and frees any instantiated node. Loaded scenes are dropped
    /// (already retrieved from the threaded loader, so nothing is orphaned).</summary>
    private void DiscardSlot(Slot slot)
    {
        if (!Engine.IsEditorHint() && slot.Root is { } root && IsInstanceValid(root)) GameSaveService.Remember(root);
        if (slot.Root is not null) PapyrusRuntime.Find(this)?.DetachCell(slot.Root);
        ReleaseActors(slot.Root);
        OnSlotDiscarded(slot);
        var dict = slot.IsPersistent ? _persSlots : _cellSlots;
        if (dict.TryGetValue(slot.Key, out var cur) && ReferenceEquals(cur, slot))
            dict.Remove(slot.Key);
        if (slot.Root != null) { slot.Root.QueueFree(); slot.Root = null; }
        ReleaseScene(slot);
    }

    private static void HoldScene(Slot slot, PackedScene scene)
    {
        slot.Loaded = scene;
        SceneHolders[scene] = SceneHolders.GetValueOrDefault(scene) + 1;
    }

    private void NoteCellLoadFailure(Slot slot)
    {
        _failedCellLoads.Add(slot.ResPath);
    }

    private static void ReleaseScene(Slot slot)
    {
        var scene = slot.Loaded;
        slot.Loaded = null;
        if (scene == null) return;
        if (SceneHolders.TryGetValue(scene, out int holders) && holders > 1)
        {
            SceneHolders[scene] = holders - 1;
            return;
        }
        SceneHolders.Remove(scene);
        scene.Dispose();
    }

    private Node? InstantiateSlot(Slot slot)
    {
        try
        {
            if (slot.Loaded != null && IsInstanceValid(slot.Loaded)) return slot.Loaded.Instantiate();
        }
        catch (ObjectDisposedException) { }
        ReleaseScene(slot);
        var fresh = ResourceLoader.Load<PackedScene>(slot.ResPath, cacheMode: _flushReplaceOnce ? ResourceLoader.CacheMode.Replace : ResourceLoader.CacheMode.Reuse);
        if (fresh != null) HoldScene(slot, fresh);
        return slot.Loaded?.Instantiate();
    }

    private void BumpEpoch()
    {
        _epoch++;
        // Pending work is stamped with the old epoch; clear the queues and drop not-yet-started slots so
        // they get re-prioritised for the new focus. In-flight/resident slots are kept: still-wanted ones
        // have their epoch refreshed in EnsureQueued next recompute, the rest fall out by distance.
        _loadQueue.Clear();
        // The ready queue holds slots that already left _inflight (Loading, scene retrieved). Nothing else
        // references them, so a plain Clear() would strand them in the dict forever (EnsureQueued would keep
        // seeing an existing entry and only refresh the epoch, never re-integrate) -> permanent holes after a
        // teleport. DiscardSlot removes each from its dict; RecomputeDesired then re-queues the still-wanted
        // ones, and the warm ResourceCache makes that re-load nearly free.
        while (_readyQueue.Count > 0) DiscardSlot(_readyQueue.Dequeue());
        DropQueuedSlots(_cellSlots);
        DropQueuedSlots(_persSlots);
        _hasLastCell = false;
        _needsRecompute = true;
    }

    private static void DropQueuedSlots(Dictionary<Vector2I, Slot> slots)
    {
        List<Vector2I>? remove = null;
        foreach (var kv in slots)
            if (kv.Value.State == SlotState.Queued)
                (remove ??= new List<Vector2I>()).Add(kv.Key);
        if (remove != null)
            foreach (var k in remove) slots.Remove(k);
    }

    /// <summary>Frees every streamed node and resets all streaming state.</summary>
    public void ClearAll()
    {
        NpcPuppetSpawner.ResetBakeState();
        ParkPapyrusCells();
        ClearHlodPresence();
        foreach (var s in _cellSlots.Values) { ReleaseActors(s.Root); s.Root?.QueueFree(); ReleaseScene(s); }
        foreach (var s in _persSlots.Values) { ReleaseActors(s.Root); s.Root?.QueueFree(); ReleaseScene(s); }
        foreach (var s in _readyQueue) ReleaseScene(s);
        _cellSlots.Clear();
        _persSlots.Clear();
        _loadQueue.Clear();
        _readyQueue.Clear();
        foreach (var s in _inflight) _abandoned.Add(s.ResPath);
        _inflight.Clear();
        HandOffAbandoned();
        _evictQueue.Clear();
        _failedCellLoads.Clear();
        _hasLastCell = false;
        _needsRecompute = true;
        _lastIntegrateMs = 0;
    }

    private void ParkPapyrusCells()
    {
        var runtime = PapyrusRuntime.Find(this);
        foreach (var slot in _cellSlots.Values)
            if (slot.Root is not null) runtime?.DetachCell(slot.Root);
        foreach (var slot in _persSlots.Values)
            if (slot.Root is not null) runtime?.DetachCell(slot.Root);
    }

    internal void ReattachPapyrusCells()
    {
        var runtime = PapyrusRuntime.Find(this);
        foreach (var slot in _cellSlots.Values)
            if (slot.Root is not null) runtime?.AttachCell(slot.ResPath, slot.Root);
        foreach (var slot in _persSlots.Values)
            if (slot.Root is not null) runtime?.AttachCell(slot.ResPath, slot.Root);
    }

    private void DoFlushReload()
    {
        RememberResidentActors();
        // One-shot Replace load, to blow past a stale ResourceCache entry (see CELL_VIEWER_NOTES.md).
        ClearAll();
        _flushReplaceOnce = true;
        _needsRecompute = true;
        SetProcess(_manifest != null);
    }
}
