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

    private sealed record RetainedState(string Blob, IReadOnlyDictionary<ulong, ulong> Remap, HashSet<InstanceKey> Done);

    private sealed record AwaitingRestore(RetainedState State, InteropOwner Owner, InstanceKey Key);

    private RetainedState? _retained;
    private readonly Dictionary<ScriptInstance, (InteropOwner Owner, InstanceKey Key)> _identities = new();
    private readonly Dictionary<ScriptInstance, AwaitingRestore> _awaiting = new();
    private readonly List<string> _lateRestoreRefusals = new();

    public int LateRestoredInstances { get; private set; }

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
            _retained = new RetainedState(blob, remap, _identities.Values.Select(entry => entry.Key).ToHashSet());
        return report;
    }

    // Invariant: nothing kept from an earlier load is applied to an instance that attaches after this call.
    public void DropRetainedState()
    {
        _retained = null;
        _awaiting.Clear();
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
        }
        catch (Exception refusal)
        {
            _lateRestoreRefusals.Add($"{instance.ScriptName} on {pending.Owner.BaseFormKey}: {refusal.Message}");
        }
    }
}
