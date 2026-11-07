#if RADICLE_HOST_AVAILABLE
using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed class HostObjectTable
{
    private const ulong EngineHandleBit = 0x8000000000000000UL;

    private readonly Dictionary<ulong, ScriptInstance> _instances = new();
    private readonly Dictionary<ScriptInstance, ulong> _instanceHandles = new(ReferenceEqualityComparer.Instance);
    private readonly List<EngineObjectReference> _engines = new();
    private readonly Dictionary<string, ulong> _engineHandles = new(StringComparer.Ordinal);

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

    public PapyrusObjectRef Describe(object? reference) => reference switch
    {
        ScriptInstance instance => new PapyrusObjectRef(HandleOf(instance), instance.ScriptName, ""),
        EngineObjectReference engine => new PapyrusObjectRef(Intern(engine), "", ""),
        _ => throw new NotSupportedException($"Papyrus object reference type {reference?.GetType().Name ?? "null"} has no host handle."),
    };

    public object Resolve(PapyrusObjectRef reference)
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

    private ulong Intern(EngineObjectReference reference)
    {
        if (_engineHandles.TryGetValue(reference.Identity, out var handle))
        {
            return handle;
        }
        _engines.Add(reference);
        handle = EngineHandleBit | (ulong)_engines.Count;
        _engineHandles.Add(reference.Identity, handle);
        return handle;
    }
}
#endif
