using OpenCommonwealth.Services.Papyrus.Owners;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using InteropObjectRef = Radicle.Interop.PapyrusObjectRef;
using RNative = Radicle.Interop.Native;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed partial class RadicleScriptHost
{
    // Constraint: call after every real native is registered; a later registration of the same native replaces the stub.
    public int PublishUnboundRefusals()
    {
        if (_callbackDepth > 0) throw new InvalidOperationException("natives cannot be published inside a native callback");
        var published = 0;
        foreach (var (native, isGlobal) in _declaredNatives)
        {
            if (FindRegistration(native) is not null || _unpublished.ContainsKey(native) || _refusalStubs.Contains(native)) continue;
            try
            {
                var flags = isGlobal ? RNative.PapyrusNativeFlags.MayWait : RNative.PapyrusNativeFlags.MayWait | RNative.PapyrusNativeFlags.AcceptScriptInstance;
                var kind = isGlobal ? NativeTargetKind.Static : NativeTargetKind.EngineObject;
                _runtime.RegisterNativeEx(native.Script, native.Function, (RNative.PapyrusNativeKind)(int)kind, string.Empty, (uint)flags);
                _refusalStubs.Add(native);
                published++;
            }
            catch (Exception failure)
            {
                LastRegistrationFailure = failure.Message;
            }
        }
        return published;
    }

    private readonly HashSet<NativeId> _refusalStubs = new();

    private (bool Handled, object? Value) ReadEngineProperty(InteropObjectRef target, string name)
    {
        if (EngineScriptHolding(target, name, out var describe) is not { } instance)
        {
            LastNativeFailure = $"the property {name} was read on {describe} and no attached script declares it";
            return (false, null);
        }
        var handle = _table.HandleOf(instance);
        var value = ReadHeldProperty(instance, handle, name);
        _lastNativeAnswer = $"property {name} on {describe} answered {(value is null ? "None" : "a value")}";
        return (true, value);
    }

    private object? ReadHeldProperty(ScriptInstance instance, ulong handle, string name)
    {
        if (_runtime.TryGetProperty(handle, name, out var bound)) return bound;
        var declared = DeclaredScalars(instance.ScriptName)
            .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
        if (declared.Name is null) return null;
        return _runtime.TryGetVariable(handle, declared.AutoVariable, out var stored) && stored is not null ? stored : declared.Default;
    }

    private bool WriteEngineProperty(InteropObjectRef target, string name, object? value)
    {
        if (EngineScriptHolding(target, name, out var describe) is not { } instance)
        {
            LastNativeFailure = $"the property {name} was written on {describe} and no attached script declares it";
            return false;
        }
        var declared = DeclaredScalars(instance.ScriptName)
            .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
        if (declared.Name is null || Normalize(declared.Type, value) is not { } normalized)
        {
            LastNativeFailure = $"the property {name} on {describe} is not a scalar auto property the host can write";
            return false;
        }
        _runtime.SetVariable(_table.HandleOf(instance), declared.AutoVariable, normalized);
        return true;
    }

    private ScriptInstance? EngineScriptHolding(InteropObjectRef target, string property, out string describe)
    {
        describe = $"handle {target.Handle} of type {target.Type}";
        if (Resolve(target) is not EngineObjectReference engine) return null;
        describe = $"{engine.Identity} ({(target.Type.Length == 0 ? "untyped" : target.Type)})";
        return _instances.FirstOrDefault(candidate => !candidate.Detached && candidate.Owner is { } owner
            && (OwnsReference(owner, engine)
                || string.Equals(owner.BaseFormKey, engine.Identity, StringComparison.OrdinalIgnoreCase))
            && DeclaredPropertyType(candidate.ScriptName, property).Length > 0);
    }

    // Invariant: only a script named exactly like the cast target resolves, so a cast to a native type never swaps an engine handle for a script that merely extends it.
    private ulong? CastEngineHandle(InteropObjectRef target, string targetType)
    {
        if (Resolve(target) is not EngineObjectReference engine) return null;
        var held = _instances.FirstOrDefault(candidate => !candidate.Detached
            && string.Equals(candidate.ScriptName, targetType, StringComparison.OrdinalIgnoreCase)
            && candidate.Owner is { } owner
            && (OwnsReference(owner, engine)
                || owner.Kind == PapyrusOwnerKind.Form && string.Equals(owner.BaseFormKey, engine.Identity, StringComparison.OrdinalIgnoreCase)));
        return held is null ? null : _table.HandleOf(held);
    }

    private string? TypeOfLookedUp(EngineObjectReference engine, string known)
    {
        var owned = _instances.Where(candidate => !candidate.Detached && candidate.Owner is { } owner
            && (OwnsReference(owner, engine)
                || owner.Kind == PapyrusOwnerKind.Form && string.Equals(owner.BaseFormKey, engine.Identity, StringComparison.OrdinalIgnoreCase))
            && _parents.ContainsKey(candidate.ScriptName)).Select(candidate => candidate.ScriptName).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToList();
        return owned.Count switch { 0 => known, 1 => owned[0], _ => null };
    }

    private string UntypedHandleNote(EngineObjectReference engine)
    {
        if (engine.ScriptType is not { } known || !_parents.ContainsKey(known)) return "";
        var names = AttachedScriptNames(engine);
        return names.Count < 2 ? "" : $" (untyped handle: the scripts {string.Join(", ", names)} are attached to it and Radicle cannot call a script function on an untyped handle, so a FunctionNotFound right after this answer is such a call)";
    }

    private List<string> AttachedScriptNames(EngineObjectReference engine) => _instances.Where(candidate => !candidate.Detached && candidate.Owner is { } owner
        && (OwnsReference(owner, engine) || owner.Kind == PapyrusOwnerKind.Form && string.Equals(owner.BaseFormKey, engine.Identity, StringComparison.OrdinalIgnoreCase))
        && _parents.ContainsKey(candidate.ScriptName)).Select(candidate => candidate.ScriptName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // Invariant: a placed or dynamic reference owner owns the bare reference key and its own owner key alike, the same merge HostObjectTable makes, so every path types one reference one way.
    private static bool OwnsReference(PapyrusOwnerId owner, EngineObjectReference engine) =>
        string.Equals(owner.Key, engine.Identity, StringComparison.OrdinalIgnoreCase)
        || owner.Kind is PapyrusOwnerKind.PlacedReference or PapyrusOwnerKind.DynamicReference
            && string.Equals(owner.Discriminator, HostObjectTable.CanonicalKey(engine), StringComparison.Ordinal);
}
