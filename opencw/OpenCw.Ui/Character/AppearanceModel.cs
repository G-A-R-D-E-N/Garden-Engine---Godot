using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed class AppearanceModel
{
    public const int MaxUndo = 64;

    private const int PresetVersion = 1;

    private readonly AppearanceControl[] _controls;
    private readonly Dictionary<string, int> _index = new();
    private readonly int[] _mirror;
    private readonly Random _rng;
    private readonly IAppearancePresetStore _store;
    private readonly double[] _original;
    private readonly List<double[]> _undo = new();
    private readonly Stack<double[]> _redo = new();
    private double[] _values;
    private string? _coalesceId;
    private bool _showOriginal;

    public AppearanceModel(IReadOnlyList<AppearanceControl> controls, Random? rng = null,
                           IAppearancePresetStore? store = null, IReadOnlyDictionary<string, double>? initial = null)
    {
        _controls = controls.ToArray();
        for (var i = 0; i < _controls.Length; i++)
        {
            var c = _controls[i];
            if (c.Min > c.Max) throw new ArgumentException($"Control {c.Id} has Min above Max.", nameof(controls));
            if (c.Default < c.Min || c.Default > c.Max) throw new ArgumentException($"Control {c.Id} default is out of range.", nameof(controls));
            if (c.Step < 0) throw new ArgumentException($"Control {c.Id} has a negative step.", nameof(controls));
            if (!_index.TryAdd(c.Id, i)) throw new ArgumentException($"Duplicate control id {c.Id}.", nameof(controls));
        }
        _mirror = Enumerable.Repeat(-1, _controls.Length).ToArray();
        for (var i = 0; i < _controls.Length; i++)
        {
            var c = _controls[i];
            if (c.MirrorId is null) continue;
            if (!_index.TryGetValue(c.MirrorId, out var j) || j == i) throw new ArgumentException($"Control {c.Id} has an invalid mirror.", nameof(controls));
            var other = _controls[j];
            if (other.Min != c.Min || other.Max != c.Max) throw new ArgumentException($"Control {c.Id} and its mirror differ in range.", nameof(controls));
            if ((_mirror[i] >= 0 && _mirror[i] != j) || (_mirror[j] >= 0 && _mirror[j] != i)) throw new ArgumentException($"Control {c.Id} has a conflicting mirror.", nameof(controls));
            _mirror[i] = j;
            _mirror[j] = i;
        }
        _rng = rng ?? Random.Shared;
        _store = store ?? new InMemoryAppearancePresetStore();
        _values = _controls.Select(c => initial is not null && initial.TryGetValue(c.Id, out var v) ? c.Normalize(v) : c.Default).ToArray();
        _original = (double[])_values.Clone();
        Controls = new MenuList<AppearanceControl>(_controls, c => c.Id, c => c.Label, c => c.Category);
        Categories = _controls.Select(c => c.Category).Distinct().ToArray();
    }

    public event Action? Changed;

    public MenuList<AppearanceControl> Controls { get; }

    public IReadOnlyList<string> Categories { get; }

    public bool Symmetric { get; set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool IsDirty => !_values.SequenceEqual(_original);

    public IReadOnlyList<string> PresetNames => _store.Names();

    public bool ShowOriginal
    {
        get => _showOriginal;
        set
        {
            if (_showOriginal == value) return;
            _showOriginal = value;
            Changed?.Invoke();
        }
    }

    public IReadOnlyDictionary<string, double> Values => Snapshot(_values);

    public IReadOnlyDictionary<string, double> OriginalValues => Snapshot(_original);

    public IReadOnlyList<string> ChangedFromOriginal =>
        _controls.Where((c, i) => _values[i] != _original[i]).Select(c => c.Id).ToArray();

    public double Get(string id) => _values[IndexOf(id)];

    public double Displayed(string id) => (_showOriginal ? _original : _values)[IndexOf(id)];

    public IReadOnlyList<AppearanceControl> ControlsIn(string category) =>
        _controls.Where(c => c.Category == category).ToArray();

    public bool Set(string id, double value, bool coalesce = false)
    {
        var i = IndexOf(id);
        var next = (double[])_values.Clone();
        Assign(next, i, value);
        if (next.SequenceEqual(_values)) return false;
        Commit(next, coalesce ? id : null);
        return true;
    }

    public bool Randomise(string? category = null)
    {
        var next = (double[])_values.Clone();
        var done = new bool[_controls.Length];
        for (var i = 0; i < _controls.Length; i++)
        {
            var c = _controls[i];
            if (done[i] || (category is not null && c.Category != category)) continue;
            var raw = c.Options is { } options ? _rng.Next(options.Count) : c.Min + _rng.NextDouble() * (c.Max - c.Min);
            Assign(next, i, raw);
            done[i] = true;
            if (Symmetric && _mirror[i] >= 0) done[_mirror[i]] = true;
        }
        if (next.SequenceEqual(_values)) return false;
        Commit(next, null);
        return true;
    }

    public bool Reset(string? category = null)
    {
        var next = (double[])_values.Clone();
        for (var i = 0; i < _controls.Length; i++)
        {
            if (category is null || _controls[i].Category == category) Assign(next, i, _controls[i].Default);
        }
        if (next.SequenceEqual(_values)) return false;
        Commit(next, null);
        return true;
    }

    public bool RevertToOriginal()
    {
        if (!IsDirty) return false;
        Commit((double[])_original.Clone(), null);
        return true;
    }

    public void EndCoalesce() => _coalesceId = null;

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(_values);
        _values = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _coalesceId = null;
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        PushUndo(_values);
        _values = _redo.Pop();
        _coalesceId = null;
        Changed?.Invoke();
        return true;
    }

    public string ExportJson() => JsonSerializer.Serialize(new { version = PresetVersion, values = Values });

    public bool TryImportJson(string json, out string error)
    {
        error = string.Empty;
        var parsed = new Dictionary<string, double>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
            {
                error = "Preset has no values object.";
                return false;
            }
            if (root.TryGetProperty("version", out var version) && !(version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var v) && v == PresetVersion))
            {
                error = "Preset version is not supported.";
                return false;
            }
            foreach (var property in values.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out var d) || !double.IsFinite(d))
                {
                    error = $"Value for {property.Name} is not a finite number.";
                    return false;
                }
                parsed[property.Name] = d;
            }
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
        var next = (double[])_values.Clone();
        foreach (var (id, value) in parsed)
        {
            if (_index.TryGetValue(id, out var i)) next[i] = _controls[i].Normalize(value);
        }
        if (!next.SequenceEqual(_values)) Commit(next, null);
        return true;
    }

    public bool SavePreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return _store.Save(name.Trim(), ExportJson());
    }

    public bool LoadPreset(string name) =>
        _store.Load(name) is { } json && TryImportJson(json, out _);

    public bool DeletePreset(string name) => _store.Delete(name);

    private int IndexOf(string id) =>
        _index.TryGetValue(id, out var i) ? i : throw new ArgumentException($"Unknown control id {id}.", nameof(id));

    private IReadOnlyDictionary<string, double> Snapshot(double[] values)
    {
        var map = new Dictionary<string, double>();
        for (var i = 0; i < _controls.Length; i++) map[_controls[i].Id] = values[i];
        return map;
    }

    private void Assign(double[] target, int i, double raw)
    {
        var value = _controls[i].Normalize(raw);
        target[i] = value;
        if (Symmetric && _mirror[i] >= 0) target[_mirror[i]] = value;
    }

    private void PushUndo(double[] values)
    {
        _undo.Add(values);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
    }

    private void Commit(double[] next, string? coalesceId)
    {
        if (coalesceId is null || coalesceId != _coalesceId) PushUndo(_values);
        _redo.Clear();
        _values = next;
        _coalesceId = coalesceId;
        _showOriginal = false;
        Changed?.Invoke();
    }
}
