using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Settings;

public readonly record struct LiveBinding<T>(T Handle, ObservedBinding Shape);

public interface IBindingEngine<T> where T : class
{
    IReadOnlyList<LiveBinding<T>> Read(string action);

    void Erase(string action, T handle);

    void Add(string action, T handle);

    T? Create(PlannedBinding planned);
}

public sealed class KeyBindingApplierCore<T> where T : class
{
    private readonly Dictionary<(string Action, InputDevice Device), List<T>> _original = new();

    public void Apply(InputTarget target, IReadOnlyList<InputMapEntry> entries, IBindingEngine<T> engine)
    {
        var key = (target.ActionName, target.Device);
        var live = engine.Read(target.ActionName);
        var current = Owned(target.Device, live);
        if (!_original.TryGetValue(key, out var original))
        {
            original = current;
            _original[key] = original;
            if (!target.Overridden)
                return;
        }
        foreach (var handle in current)
            engine.Erase(target.ActionName, handle);
        if (!target.Overridden)
        {
            foreach (var handle in original)
                engine.Add(target.ActionName, handle);
            return;
        }
        foreach (var planned in KeyBindingEvents.Creations(target.ActionName, target.Device, entries))
        {
            if (engine.Create(planned) is { } created)
                engine.Add(target.ActionName, created);
        }
    }

    private static List<T> Owned(InputDevice device, IReadOnlyList<LiveBinding<T>> live)
    {
        var shapes = new ObservedBinding[live.Count];
        for (var index = 0; index < live.Count; index++)
            shapes[index] = live[index].Shape;
        var owned = new List<T>();
        foreach (var index in KeyBindingEvents.ManagedIndexes(device, shapes))
            owned.Add(live[index].Handle);
        return owned;
    }
}
