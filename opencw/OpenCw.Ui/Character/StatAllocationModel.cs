using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed record StatDef(string Id, string Label, int Min, int Max, int PointCost = 1, string Description = "");

public sealed record StatPreset(string Name, IReadOnlyDictionary<string, int> Values);

public enum AllocationConfirm { Confirmed, UnspentPoints, Invalid, AlreadyFinished }

public sealed record AllocationValidation(bool IsValid, int Remaining, IReadOnlyList<string> Errors);

public sealed class StatAllocationModel
{
    public const int MaxUndo = 64;

    private readonly StatDef[] _defs;
    private readonly Dictionary<string, int> _index = new();
    private readonly StatPreset[] _presets;
    private readonly bool _requireAllSpent;
    private readonly List<int[]> _undo = new();
    private readonly Stack<int[]> _redo = new();
    private int[] _values;

    public StatAllocationModel(IReadOnlyList<StatDef> stats, int pool, bool requireAllSpent = true,
                               IReadOnlyList<StatPreset>? presets = null)
    {
        if (stats.Count == 0) throw new ArgumentException("At least one stat is required.", nameof(stats));
        if (pool < 0) throw new ArgumentOutOfRangeException(nameof(pool));
        _defs = stats.ToArray();
        for (var i = 0; i < _defs.Length; i++)
        {
            var def = _defs[i];
            if (def.Min > def.Max) throw new ArgumentException($"Stat {def.Id} has Min above Max.", nameof(stats));
            if (def.PointCost < 1) throw new ArgumentException($"Stat {def.Id} has a point cost below 1.", nameof(stats));
            if (!_index.TryAdd(def.Id, i)) throw new ArgumentException($"Duplicate stat id {def.Id}.", nameof(stats));
        }
        Pool = pool;
        _requireAllSpent = requireAllSpent;
        _presets = presets?.ToArray() ?? Array.Empty<StatPreset>();
        _values = _defs.Select(d => d.Min).ToArray();
        Stats = new MenuList<StatDef>(_defs, d => d.Id, d => d.Label);
    }

    public event Action? Changed;

    public MenuList<StatDef> Stats { get; }

    public int Pool { get; }

    public int Spent => SpentOf(_values);

    public int Remaining => Pool - Spent;

    public bool IsConfirmed { get; private set; }

    public bool IsCancelled { get; private set; }

    public bool IsFinished => IsConfirmed || IsCancelled;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public IReadOnlyList<StatPreset> Presets => _presets;

    public IReadOnlyDictionary<string, int> Values =>
        _defs.Select((d, i) => (d.Id, _values[i])).ToDictionary(p => p.Id, p => p.Item2);

    public int Value(string id) => _values[IndexOf(id)];

    public bool CanIncrement(string id) =>
        !IsFinished && _index.TryGetValue(id, out var i) && _values[i] < _defs[i].Max && _defs[i].PointCost <= Remaining;

    public bool CanDecrement(string id) =>
        !IsFinished && _index.TryGetValue(id, out var i) && _values[i] > _defs[i].Min;

    public bool Increment(string id) => _index.TryGetValue(id, out var i) && SetValue(id, _values[i] + 1);

    public bool Decrement(string id) => _index.TryGetValue(id, out var i) && SetValue(id, _values[i] - 1);

    public bool SetValue(string id, int value)
    {
        if (IsFinished || !_index.TryGetValue(id, out var i)) return false;
        if (value < _defs[i].Min || value > _defs[i].Max || value == _values[i]) return false;
        var next = (int[])_values.Clone();
        next[i] = value;
        if (SpentOf(next) > Pool) return false;
        Commit(next);
        return true;
    }

    public bool Reset()
    {
        var mins = _defs.Select(d => d.Min).ToArray();
        if (IsFinished || mins.SequenceEqual(_values)) return false;
        Commit(mins);
        return true;
    }

    public bool ApplyPreset(string name)
    {
        var preset = _presets.FirstOrDefault(p => p.Name == name);
        if (IsFinished || preset is null) return false;
        if (preset.Values.Keys.Any(k => !_index.ContainsKey(k))) return false;
        var next = _defs.Select(d => preset.Values.TryGetValue(d.Id, out var v) ? v : d.Min).ToArray();
        for (var i = 0; i < next.Length; i++)
        {
            if (next[i] < _defs[i].Min || next[i] > _defs[i].Max) return false;
        }
        if (SpentOf(next) > Pool || next.SequenceEqual(_values)) return false;
        Commit(next);
        return true;
    }

    public bool Undo()
    {
        if (IsFinished || _undo.Count == 0) return false;
        _redo.Push(_values);
        _values = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (IsFinished || _redo.Count == 0) return false;
        PushUndo(_values);
        _values = _redo.Pop();
        Changed?.Invoke();
        return true;
    }

    public AllocationValidation Validate()
    {
        var errors = StructuralErrors();
        if (_requireAllSpent && Remaining > 0) errors.Add($"{Remaining} points unspent.");
        return new AllocationValidation(errors.Count == 0, Remaining, errors);
    }

    public AllocationConfirm TryConfirm()
    {
        if (IsFinished) return AllocationConfirm.AlreadyFinished;
        if (StructuralErrors().Count > 0) return AllocationConfirm.Invalid;
        if (_requireAllSpent && Remaining > 0) return AllocationConfirm.UnspentPoints;
        IsConfirmed = true;
        Changed?.Invoke();
        return AllocationConfirm.Confirmed;
    }

    public bool Cancel()
    {
        if (IsFinished) return false;
        IsCancelled = true;
        Changed?.Invoke();
        return true;
    }

    private int IndexOf(string id) =>
        _index.TryGetValue(id, out var i) ? i : throw new ArgumentException($"Unknown stat id {id}.", nameof(id));

    private int SpentOf(int[] values)
    {
        var total = 0;
        for (var i = 0; i < values.Length; i++) total += (values[i] - _defs[i].Min) * _defs[i].PointCost;
        return total;
    }

    private List<string> StructuralErrors()
    {
        var errors = new List<string>();
        for (var i = 0; i < _values.Length; i++)
        {
            if (_values[i] < _defs[i].Min || _values[i] > _defs[i].Max) errors.Add($"{_defs[i].Id} is out of range.");
        }
        if (Spent > Pool) errors.Add("More points spent than the pool holds.");
        return errors;
    }

    private void PushUndo(int[] values)
    {
        _undo.Add(values);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
    }

    private void Commit(int[] next)
    {
        PushUndo(_values);
        _redo.Clear();
        _values = next;
        Changed?.Invoke();
    }
}
