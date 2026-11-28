using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using InteropObjectRef = Radicle.Interop.PapyrusObjectRef;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed class HostObjectTable
{
    private const ulong EngineHandleBit = 0x8000000000000000UL;

    private readonly Dictionary<ulong, ScriptInstance> _instances = new();
    private readonly Dictionary<ScriptInstance, ulong> _instanceHandles = new(ReferenceEqualityComparer.Instance);
    private readonly List<EngineObjectReference> _engines = new();
    private readonly Dictionary<string, ulong> _engineHandles = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, string> _types = new();

    public Func<string, string, bool>? Derives { get; set; }

    public void Register(ScriptInstance instance, ulong id)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if ((id & EngineHandleBit) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "A script instance handle must leave the engine bit clear.");
        }
        if (_instances.ContainsKey(id))
        {
            throw new InvalidOperationException($"Script instance handle {id} is already registered.");
        }
        if (_instanceHandles.ContainsKey(instance))
        {
            throw new InvalidOperationException("The script instance is already registered.");
        }
        _instances.Add(id, instance);
        _instanceHandles.Add(instance, id);
    }

    public void Unregister(ScriptInstance instance)
    {
        if (!_instanceHandles.Remove(instance, out var id)) return;
        _instances.Remove(id);
    }

    public bool TryGetInstance(ulong id, out ScriptInstance? instance) => _instances.TryGetValue(id, out instance);

    public ulong HandleOf(ScriptInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_instanceHandles.TryGetValue(instance, out var id))
        {
            return id;
        }
        throw new KeyNotFoundException("The script instance is not registered.");
    }

    public InteropObjectRef Describe(object? reference, string? engineType = null) => reference switch
    {
        ScriptInstance instance => new InteropObjectRef(HandleOf(instance), instance.ScriptName, ""),
        EngineObjectReference engine => DescribeEngine(engine, engineType),
        _ => throw new NotSupportedException($"Papyrus object reference type {reference?.GetType().Name ?? "null"} has no host handle."),
    };

    public object Resolve(InteropObjectRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if ((reference.Handle & EngineHandleBit) != 0)
        {
            var index = reference.Handle & ~EngineHandleBit;
            if (index >= 1 && index <= (ulong)_engines.Count)
            {
                return _engines[(int)(index - 1)];
            }
        }
        else if (_instances.TryGetValue(reference.Handle, out var instance))
        {
            return instance;
        }
        throw new KeyNotFoundException($"Papyrus object handle {reference.Handle} is not registered.");
    }

    public bool TryGetEngine(ulong handle, out EngineObjectReference? reference)
    {
        reference = null;
        var index = handle & ~EngineHandleBit;
        if ((handle & EngineHandleBit) == 0 || index < 1 || index > (ulong)_engines.Count) return false;
        reference = _engines[(int)(index - 1)];
        return true;
    }

    public ulong HandleOf(EngineObjectReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return Intern(reference);
    }

    // Invariant: Radicle compares objects by handle and type name, so one reference must keep one handle and, once typed, one type.
    public string? KnownType(EngineObjectReference reference) =>
        _engineHandles.TryGetValue(CanonicalKey(reference), out var handle) && _types.TryGetValue(handle, out var type) ? type : null;

    private InteropObjectRef DescribeEngine(EngineObjectReference engine, string? engineType)
    {
        var handle = Intern(engine);
        if (string.IsNullOrEmpty(engineType)) return new InteropObjectRef(handle, "", "");
        if (!_types.TryGetValue(handle, out var known) || Derives?.Invoke(engineType, known) == true) _types[handle] = known = engineType;
        return new InteropObjectRef(handle, known, "");
    }

    // Constraint: only placed and dynamic reference owners merge with their bare reference key; other owners keep their own key.
    internal static string CanonicalKey(EngineObjectReference reference) =>
        reference.Owner is { Kind: PapyrusOwnerKind.PlacedReference or PapyrusOwnerKind.DynamicReference } owner && reference.Identity == owner.Key
            ? owner.Discriminator
            : reference.Identity;

    private ulong Intern(EngineObjectReference reference)
    {
        var key = CanonicalKey(reference);
        if (_engineHandles.TryGetValue(key, out var handle))
        {
            var index = (int)(handle & ~EngineHandleBit) - 1;
            if (reference.Owner is not null && _engines[index].Owner is null)
                _engines[index] = reference with { ScriptType = reference.ScriptType ?? _engines[index].ScriptType };
            return handle;
        }
        _engines.Add(reference);
        handle = EngineHandleBit | (ulong)_engines.Count;
        _engineHandles.Add(key, handle);
        return handle;
    }
}
