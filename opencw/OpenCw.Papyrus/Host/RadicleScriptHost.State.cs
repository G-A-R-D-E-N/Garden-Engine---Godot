using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using InstanceKey = (int Kind, string Base, string Discriminator, string Script);
using InteropObjectRef = Radicle.Interop.PapyrusObjectRef;
using InteropOwner = Radicle.Interop.PapyrusOwner;
using InteropIdentity = Radicle.Interop.PapyrusInstanceIdentity;
using InteropStateReport = Radicle.Interop.PapyrusInstanceStateReport;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed record HostEngineHandle(ulong Handle, string Identity, PapyrusOwnerId? Owner);

public sealed record HostStateCapture(string Blob, string Digest, IReadOnlyList<HostEngineHandle> Engine);

public sealed record ScriptReferenceReport(int Resolved, IReadOnlyList<string> Unresolved);

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

    private sealed record RetainedState(string Blob, IReadOnlyDictionary<ulong, ulong> Remap, HashSet<InstanceKey> Done);

    private sealed record AwaitingRestore(RetainedState State, InteropOwner Owner, InstanceKey Key);

    private RetainedState? _retained;
    private readonly Dictionary<ScriptInstance, (InteropOwner Owner, InstanceKey Key)> _identities = new();
    private readonly Dictionary<ScriptInstance, AwaitingRestore> _awaiting = new();
    private readonly List<string> _lateRestoreRefusals = new();
    private readonly List<SavedReference> _pendingReferences = new();

    public int LateRestoredInstances { get; private set; }

    public int LateResolvedReferences { get; private set; }

    public IReadOnlyList<string> LateRestoreRefusals => _lateRestoreRefusals;

    // Requires: every instance the blob should reach is attached or will attach later; Radicle refuses all or nothing, so a thrown exception leaves instance state untouched.
    public InteropStateReport RestoreState(string blob, IReadOnlyList<HostEngineHandle> engine, bool skipUnknown = true)
    {
        if (_callbackDepth > 0) throw new InvalidOperationException("the state cannot be restored inside a native callback");
        DropRetainedState();
        var remap = new Dictionary<ulong, ulong>();
        foreach (var saved in engine)
            remap[saved.Handle] = _table.HandleOf(saved.Owner is { } owner ? new EngineObjectReference(owner) : new EngineObjectReference(saved.Identity));
        var report = _runtime.RestoreState(blob, skipUnknown, remap);
        if (report.SkippedInstances > 0)
        {
            _retained = new RetainedState(blob, remap, _identities.Values.Select(entry => entry.Key).ToHashSet());
            TrackUnresolvedReferences(blob, _retained.Done);
        }
        return report;
    }

    // Invariant: nothing kept from an earlier load is applied to an instance that attaches after this call.
    public void DropRetainedState()
    {
        _retained = null;
        _awaiting.Clear();
        _pendingReferences.Clear();
    }

    private static InstanceKey KeyOf(SavedInstanceId id) => (id.Kind, id.Base, id.Discriminator, id.Script.ToLowerInvariant());

    private void TrackUnresolvedReferences(string blob, HashSet<InstanceKey> live)
    {
        try
        {
            _pendingReferences.AddRange(StateReferences.Scan(blob).Where(reference => !live.Contains(KeyOf(reference.Holder)) || !live.Contains(KeyOf(reference.Target))));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _lateRestoreRefusals.Add($"references were not scanned: {error.Message}");
        }
    }

    // Invariant: a reference is written only into a variable that still reads None, so a value the script set since the load is never replaced; a reference inside an array or struct is listed, never patched.
    public ScriptReferenceReport ResolveReferences()
    {
        var resolved = 0;
        if (_callbackDepth == 0 && _retained is { } retained)
        {
            var live = new Dictionary<InstanceKey, ScriptInstance>();
            foreach (var (instance, identity) in _identities) live[identity.Key] = instance;
            foreach (var reference in _pendingReferences.ToArray())
            {
                var holderKey = KeyOf(reference.Holder);
                if (!retained.Done.Contains(holderKey) || !live.TryGetValue(holderKey, out var holder) || !live.TryGetValue(KeyOf(reference.Target), out var target)) continue;
                if (reference.Nested) continue;
                try
                {
                    var handle = _table.HandleOf(holder);
                    if (!_runtime.TryGetVariable(handle, reference.Variable, out var current)) continue;
                    if (current is null)
                    {
                        _runtime.SetVariable(handle, reference.Variable, new InteropObjectRef(_table.HandleOf(target), reference.Type, reference.State));
                        resolved++;
                    }
                    _pendingReferences.Remove(reference);
                }
                catch (Exception refusal) when (refusal is not OutOfMemoryException)
                {
                    _lateRestoreRefusals.Add($"{reference.Name}: {refusal.Message}");
                }
            }
        }
        LateResolvedReferences += resolved;
        return new ScriptReferenceReport(resolved, _pendingReferences.Select(reference => reference.Name).ToArray());
    }

    private static InstanceKey KeyOf(InteropOwner owner, string script) =>
        ((int)owner.Kind, owner.BaseFormKey, owner.Discriminator, script.ToLowerInvariant());

    private void TrackIdentity(ScriptInstance instance, InteropOwner owner)
    {
        var key = KeyOf(owner, instance.ScriptName);
        _identities[instance] = (owner, key);
        if (_retained is { } retained && !retained.Done.Contains(key)) _awaiting[instance] = new AwaitingRestore(retained, owner, key);
    }

    private void ForgetIdentity(ScriptInstance instance)
    {
        _identities.Remove(instance);
        _awaiting.Remove(instance);
    }

    // Requires: called after OnInit completed, so the saved values land over what OnInit wrote, as a load does for an instance that was already initialised.
    private void ApplyAwaitingRestore(ScriptInstance instance)
    {
        if (!_awaiting.Remove(instance, out var pending) || !pending.State.Done.Add(pending.Key)) return;
        try
        {
            var report = _runtime.RestoreInstances(pending.State.Blob, new[] { new InteropIdentity(pending.Owner, instance.ScriptName) }, true, pending.State.Remap);
            LateRestoredInstances += report.Instances;
            var live = _identities.Values.Select(entry => entry.Key).ToHashSet();
            _pendingReferences.RemoveAll(reference => KeyOf(reference.Holder) == pending.Key && live.Contains(KeyOf(reference.Target)));
            ResolveReferences();
        }
        catch (Exception refusal)
        {
            _lateRestoreRefusals.Add($"{instance.ScriptName} on {pending.Owner.BaseFormKey}: {refusal.Message}");
        }
    }
}
