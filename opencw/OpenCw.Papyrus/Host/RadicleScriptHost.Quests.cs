using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using LifecycleInput = Radicle.Interop.QuestStartupInput;
using LifecycleProperty = Radicle.Interop.QuestLifecycleProperty;
using LifecycleRuntime = Radicle.Interop.PapyrusQuestLifecycle;

namespace OpenCommonwealth.Services.Papyrus.Host;

public sealed record HostQuestStart(bool Applied, IReadOnlyList<ScriptInstance> Instances, int AttachedScripts, string? Reason);

public sealed partial class RadicleScriptHost
{
    private LifecycleRuntime? _lifecycle;
    private (PapyrusOwnerId Owner, IReadOnlyDictionary<string, Dictionary<string, PapyrusValue>> Values)? _starting;
    private readonly List<ScriptInstance> _startingInstances = new();

    // Requires: not called inside a native callback; one quest at a time, because lifecycle-attached handles are mapped to the quest being started.
    public HostQuestStart StartQuest(string quest, IReadOnlyList<string> scripts, IReadOnlyList<LifecycleProperty> properties,
        int fragments, int aliases, IReadOnlyDictionary<string, Dictionary<string, PapyrusValue>> values)
    {
        if (_callbackDepth > 0) throw new InvalidOperationException("a quest cannot start inside a native callback");
        _startingInstances.Clear();
        var tokensBefore = new HashSet<long>(_latentTokens.Keys);
        Radicle.Interop.QuestStartupResult result;
        string? failure = null;
        var reasons = new List<string>();
        try
        {
            ReplayUnpublished();
            _starting = (PapyrusOwnerId.ForForm(quest), values);
            _lifecycle ??= new LifecycleRuntime(_runtime);
            result = _lifecycle.Start(new[] { new LifecycleInput(quest, true, scripts, fragments, aliases, null, properties) });
        }
        catch (Exception error)
        {
            failure = error.Message;
            reasons.Add($"{quest}: {error.Message}");
            try { _lifecycle?.Stop(quest); }
            catch (Exception stop) { reasons.Add($"{quest}: stop failed: {stop.Message}"); }
            result = new Radicle.Interop.QuestStartupResult(false, 0, 0, reasons);
        }
        finally
        {
            _starting = null;
        }
        var adopted = _startingInstances.ToArray();
        _startingInstances.Clear();
        if (!result.Applied)
        {
            foreach (var added in _latentTokens.Keys.Where(key => !tokensBefore.Contains(key)).ToList())
                _latentTokens.Remove(added);
            foreach (var instance in adopted)
            {
                instance.Lifecycle = ScriptLifecycle.Failed;
                instance.Failure = failure ?? string.Join("; ", result.Reasons);
                instance.Detached = true;
            }
            return new HostQuestStart(false, adopted, result.AttachedScripts, string.Join("; ", result.Reasons));
        }
        var instances = new List<ScriptInstance>();
        var handles = _lifecycle.InstancesFor(quest);
        for (var i = 0; i < handles.Count; ++i)
        {
            if (!_table.TryGetInstance(handles[i], out var instance) || instance is null)
            {
                _starting = (PapyrusOwnerId.ForForm(quest), values);
                instance = TryAdopt(handles[i]);
                _starting = null;
                _startingInstances.Clear();
            }
            if (instance is null) continue;
            instance.Lifecycle = ToLifecycle(_runtime.Lifecycle(handles[i]));
            instance.Failure = _runtime.Failure(handles[i]);
            instances.Add(instance);
        }
        return new HostQuestStart(true, instances, result.AttachedScripts, null);
    }

    // Constraint: Radicle accepts a parent chain only if it ends in a loaded script with no parent; the direct path also accepts ScriptObject as the end.
    public bool ChainEndsInLoadedRoot(string script)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (_parents.TryGetValue(script, out var parent))
        {
            if (!visited.Add(script)) return false;
            if (string.IsNullOrEmpty(parent)) return true;
            script = parent;
        }
        return false;
    }

    public void StopQuest(string quest) => _lifecycle?.Stop(quest);

    private ScriptInstance? TryAdopt(ulong handle)
    {
        if (_starting is not { } start) return null;
        var script = _runtime.InstanceScript(handle);
        if (script is null) return null;
        IReadOnlyDictionary<string, PapyrusValue> values = start.Values.TryGetValue(script, out var bound)
            ? bound : new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
        var instance = Wrap(handle, script, values, start.Owner);
        instance.Lifecycle = ScriptLifecycle.Initializing;
        _startingInstances.Add(instance);
        return instance;
    }
}
