using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using InstanceKey = (int Kind, string Base, string Discriminator, string Script);
using InteropOwner = Radicle.Interop.PapyrusOwner;
using InteropIdentity = Radicle.Interop.PapyrusInstanceIdentity;
using InteropStateReport = Radicle.Interop.PapyrusInstanceStateReport;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed record HostEngineHandle(ulong Handle, string Identity, PapyrusOwnerId? Owner);

public sealed record HostStateCapture(string Blob, string Digest, IReadOnlyList<HostEngineHandle> Engine);

public sealed partial class RadicleScriptHost
{
    // Requires: not called inside a native callback; an engine handle in the blob that the table does not know is a KeyNotFoundException.
    public HostStateCapture CaptureState()
    {
        if (_callbackDepth > 0) throw new InvalidOperationException("the state cannot be captured inside a native callback");
        var capture = _runtime.CaptureState();
        var engine = capture.EngineHandles.Select(handle => _table.TryGetEngine(handle, out var reference) && reference is not null
            ? new HostEngineHandle(handle, reference.Identity, reference.Owner)
            : throw new KeyNotFoundException($"the engine handle {handle} is not in the host table")).ToArray();
        return new HostStateCapture(capture.Blob, capture.Digest, engine);
    }

    internal sealed record RetainedState(string Blob, IReadOnlyList<HostEngineHandle> Engine, IReadOnlyDictionary<ulong, ulong> Remap, HashSet<InstanceKey> Done);

    internal sealed record AwaitingRestore(IReadOnlyList<RetainedState> States, InteropOwner Owner, InstanceKey Key);

    public sealed class RetainedStateMemento
    {
        internal RetainedStateMemento(IReadOnlyList<RetainedState> retained, IReadOnlyDictionary<ScriptInstance, AwaitingRestore> awaiting)
        {
            Retained = retained;
            Awaiting = awaiting;
        }

        internal IReadOnlyList<RetainedState> Retained { get; }

        internal IReadOnlyDictionary<ScriptInstance, AwaitingRestore> Awaiting { get; }
    }

    public const int MaxRetainedStates = 4;

    private readonly List<RetainedState> _retained = new();
    private readonly Dictionary<ScriptInstance, (InteropOwner Owner, InstanceKey Key)> _identities = new();
    private readonly Dictionary<ScriptInstance, AwaitingRestore> _awaiting = new();
    private readonly List<string> _lateRestoreRefusals = new();

    public int LateRestoredInstances { get; private set; }

    public IReadOnlyList<string> LateRestoreRefusals => _lateRestoreRefusals;

    public event Action<string>? LateRestoreRefused;

    // Invariant: the blobs of earlier loads that still hold instances which never attached; a save carries them so a save made before those instances attach does not lose their state.
    public IReadOnlyList<(string Blob, IReadOnlyList<HostEngineHandle> Engine)> PendingStates() =>
        _retained.Select(state => (state.Blob, state.Engine)).ToList();

    // Requires: called after RestoreState for the same load; the oldest state is dropped past MaxRetainedStates.
    public void RetainState(string blob, IReadOnlyList<HostEngineHandle> engine)
    {
        if (_callbackDepth > 0) throw new InvalidOperationException("the state cannot be retained inside a native callback");
        var remap = new Dictionary<ulong, ulong>();
        foreach (var saved in engine)
            remap[saved.Handle] = _table.HandleOf(saved.Owner is { } owner ? new EngineObjectReference(owner) : new EngineObjectReference(saved.Identity));
        _retained.Add(new RetainedState(blob, engine, remap, _identities.Values.Select(entry => entry.Key).ToHashSet()));
        if (_retained.Count > MaxRetainedStates) _retained.RemoveAt(0);
    }

    // Requires: every instance the blob should reach is attached or will attach later; Radicle refuses all or nothing, so a thrown exception leaves instance state untouched.
    public InteropStateReport RestoreState(string blob, IReadOnlyList<HostEngineHandle> engine, bool skipUnknown = true)
    {
        if (_callbackDepth > 0) throw new InvalidOperationException("the state cannot be restored inside a native callback");
        var remap = new Dictionary<ulong, ulong>();
        foreach (var saved in engine)
            remap[saved.Handle] = _table.HandleOf(saved.Owner is { } owner ? new EngineObjectReference(owner) : new EngineObjectReference(saved.Identity));
        var report = _runtime.RestoreState(blob, skipUnknown, remap);
        DropRetainedState();
        if (report.SkippedInstances > 0)
            _retained.Add(new RetainedState(blob, engine, remap, _identities.Values.Select(entry => entry.Key).ToHashSet()));
        return report;
    }

    // Invariant: nothing kept from an earlier load is applied to an instance that attaches after this call.
    public void DropRetainedState()
    {
        _retained.Clear();
        _awaiting.Clear();
    }

    // Invariant: counts only the waits whose completion can still arrive, so an abandoned wait is no longer counted.
    public int PendingLatentWaits => _latentTokens.Count;

    // Requires: not called inside a native callback; a completion for an abandoned token is refused afterwards.
    public int AbandonLatentWaits()
    {
        if (_callbackDepth > 0) throw new InvalidOperationException("latent waits cannot be abandoned inside a native callback");
        var abandoned = _latentTokens.Keys.ToList();
        _latentTokens.Clear();
        lock (_deferredLock) _deferredCompletions.Clear();
        foreach (var token in abandoned) LatentAbandoned?.Invoke(token);
        return abandoned.Count;
    }

    public RetainedStateMemento TakeRetained() => new(_retained.ToList(), new Dictionary<ScriptInstance, AwaitingRestore>(_awaiting));

    // Invariant: an instance discarded since the take is not put back as awaiting.
    public void PutRetained(RetainedStateMemento memento)
    {
        ArgumentNullException.ThrowIfNull(memento);
        _retained.Clear();
        _retained.AddRange(memento.Retained);
        _awaiting.Clear();
        foreach (var (instance, pending) in memento.Awaiting)
            if (_identities.ContainsKey(instance)) _awaiting[instance] = pending;
    }

    private static InstanceKey KeyOf(InteropOwner owner, string script) =>
        ((int)owner.Kind, owner.BaseFormKey, owner.Discriminator, script.ToLowerInvariant());

    private void TrackIdentity(ScriptInstance instance, InteropOwner owner)
    {
        var key = KeyOf(owner, instance.ScriptName);
        _identities[instance] = (owner, key);
        var open = _retained.Where(state => !state.Done.Contains(key)).ToList();
        if (open.Count > 0) _awaiting[instance] = new AwaitingRestore(open, owner, key);
    }

    private void ForgetIdentity(ScriptInstance instance)
    {
        _identities.Remove(instance);
        _awaiting.Remove(instance);
    }

    // Requires: called after OnInit completed, so the saved values land over what OnInit wrote, as a load does for an instance that was already initialised.
    private void ApplyAwaitingRestore(ScriptInstance instance)
    {
        TryLateRestore(instance);
        foreach (var other in _awaiting.Keys.Where(key => key.Lifecycle == ScriptLifecycle.Ready && !ReferenceEquals(key, instance)).ToList()) TryLateRestore(other);
    }

    // Invariant: an instance leaves the awaiting set and is marked done only when a saved state restored it; a refused restore keeps it awaiting for the next attempt and is reported through LateRestoreRefused.
    private void TryLateRestore(ScriptInstance instance)
    {
        if (!_awaiting.TryGetValue(instance, out var pending)) return;
        foreach (var state in pending.States)
        {
            if (state.Done.Contains(pending.Key)) continue;
            try
            {
                var report = _runtime.RestoreInstances(state.Blob, new[] { new InteropIdentity(pending.Owner, instance.ScriptName) }, true, state.Remap);
                if (report.Instances == 0) continue;
                LateRestoredInstances += report.Instances;
                foreach (var each in pending.States) each.Done.Add(pending.Key);
                _awaiting.Remove(instance);
                return;
            }
            catch (Exception refusal)
            {
                var message = $"{instance.ScriptName} on {pending.Owner.BaseFormKey}: {refusal.Message}";
                _lateRestoreRefusals.Add(message);
                LateRestoreRefused?.Invoke(message);
                return;
            }
        }
        _awaiting.Remove(instance);
    }
}
