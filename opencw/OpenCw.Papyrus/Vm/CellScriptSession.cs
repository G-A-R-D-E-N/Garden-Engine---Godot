using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed record CellBinding(string Cell, string Reference, string Script);

public sealed record ActivationResult(bool Reached, VmResult? Result, string? Reason)
{
    public static ActivationResult Refused(string reason) => new(false, null, reason);
}

/// <summary>
/// The one session played and streamed cells share.
/// </summary>
/// <remarks>
/// The session owns exactly one <see cref="ScriptHost"/>, so a played cell and a streamed cell reach
/// the same library, the same attached instances and the same native registrations. When a cell
/// detaches, its reference instances are parked rather than destroyed, and reattaching the cell
/// restores the same instances, so a script keeps its lifecycle, properties and queued events across
/// a stream. Activation is routed through the session to the instance the reference is bound to; an
/// activation for a parked or unknown reference is refused and counted instead of guessed.
/// <see cref="Tick"/> does nothing but hand the budget to the host, so no scheduling logic lives
/// here.
/// </remarks>
public sealed class CellScriptSession
{
    private readonly IScriptHost _host;
    private readonly Dictionary<string, List<ScriptInstance>> _byReference = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CellBinding> _byCell = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Reference, ScriptInstance Instance)>> _parked = new(StringComparer.Ordinal);

    public CellScriptSession(IScriptHost host) => _host = host;

    public IScriptHost Host => _host;

    public int BoundInstances => _byReference.Values.Sum(list => list.Count);

    public int ParkedCells => _parked.Count;

    public int ParkedInstances => _parked.Values.Sum(list => list.Count);

    public int AttachedCells => _byCell.Values.Select(binding => binding.Cell).Distinct(StringComparer.Ordinal).Count();

    public int Activations { get; private set; }

    public int ReusedInstances { get; private set; }

    public int RefusedActivations { get; private set; }

    public ScriptInstance Bind(string cell, string reference, string scriptName,
        IReadOnlyDictionary<string, PapyrusValue>? properties = null, PapyrusOwnerId? owner = null)
    {
        if (owner is { } identity && identity.Key != reference)
            throw new ArgumentException("the cell reference does not match its script owner", nameof(owner));
        if (_parked.Values.Any(list => list.Any(entry => entry.Reference == reference)))
            throw new InvalidOperationException($"the reference {reference} is parked; reattach its cell before binding");
        if (_byCell.TryGetValue(reference, out var binding) && binding.Cell != cell)
            throw new InvalidOperationException($"the reference {reference} is already attached to {binding.Cell}");
        if (_byReference.TryGetValue(reference, out var instances)
            && instances.Any(entry => string.Equals(entry.ScriptName, scriptName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"the reference {reference} already has script {scriptName}");
        var instance = _host.Attach(scriptName, properties, owner);
        if (instances is null) _byReference[reference] = instances = new List<ScriptInstance>();
        instances.Add(instance);
        _byCell[reference] = new CellBinding(cell, reference, scriptName);
        return instance;
    }

    public ScriptInstance? InstanceFor(string reference) =>
        _byReference.TryGetValue(reference, out var instances) ? instances.FirstOrDefault() : null;

    /// <summary>Parks every instance bound to the cell, keeping it for a later reattach.</summary>
    public IReadOnlyList<ScriptInstance> DetachCell(string cell)
    {
        var bound = _byCell.Where(entry => entry.Value.Cell == cell).Select(entry => entry.Key).ToList();
        var parked = _parked.TryGetValue(cell, out var existing)
            ? existing : new List<(string Reference, ScriptInstance Instance)>(bound.Count);
        foreach (var reference in bound)
        {
            if (!_byReference.TryGetValue(reference, out var instances))
            {
                continue;
            }
            foreach (var instance in instances)
            {
                parked.Add((reference, instance));
                instance.Detached = true;
            }
            _byReference.Remove(reference);
            _byCell.Remove(reference);
        }
        if (parked.Count != 0) _parked[cell] = parked;
        return parked.Select(entry => entry.Instance).ToList();
    }

    /// <summary>Restores the cell's parked instances, keeping the same objects.</summary>
    public IReadOnlyList<ScriptInstance> ReattachCell(string cell, IReadOnlySet<string>? admitted = null)
    {
        if (!_parked.TryGetValue(cell, out var parked))
        {
            return Array.Empty<ScriptInstance>();
        }
        var restored = parked.Where(entry => admitted is null || admitted.Contains(entry.Reference)).ToArray();
        foreach (var (reference, instance) in restored)
        {
            if (!_byReference.TryGetValue(reference, out var instances))
                _byReference[reference] = instances = new List<ScriptInstance>();
            instances.Add(instance);
            _byCell[reference] = new CellBinding(cell, reference, instance.ScriptName);
            instance.Detached = false;
            ReusedInstances++;
        }
        parked.RemoveAll(entry => admitted is null || admitted.Contains(entry.Reference));
        if (parked.Count == 0) _parked.Remove(cell);
        return restored.Select(entry => entry.Instance).ToList();
    }

    /// <summary>Routes an activation to the instance the reference is bound to.</summary>
    public ActivationResult Activate(string reference, string function, params PapyrusValue[] arguments)
    {
        if (!_byReference.TryGetValue(reference, out var instances))
        {
            RefusedActivations++;
            var reason = _parked.Values.Any(list => list.Count > 0)
                ? $"the reference {reference} is parked, so its cell is not attached"
                : $"the reference {reference} is not bound to this session";
            return ActivationResult.Refused(reason);
        }
        if (instances.Count != 1)
        {
            RefusedActivations++;
            return ActivationResult.Refused("a reference with multiple scripts must use owner event delivery");
        }
        Activations++;
        return new ActivationResult(true, _host.RunEvent(instances[0], function, arguments), null);
    }

    /// <summary>The only tick: it hands the budget to the host and does nothing else.</summary>
    public int Tick(int budget) => _host.RunStep(budget);
}
