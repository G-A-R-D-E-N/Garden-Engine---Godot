using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.Settings;

public sealed class SettingsModel
{
    public const string FileName = "settings.json";

    private readonly List<SettingDef> _defs;
    private readonly Dictionary<string, SettingDef> _byId = new();
    private readonly Dictionary<string, SettingValue> _applied = new();
    private readonly Dictionary<string, SettingValue> _pending = new();
    private readonly IFileStore _store;
    private readonly RevertCountdown _countdown = new();
    private readonly double _revertSeconds;
    private Dictionary<string, SettingValue>? _revertTo;

    public SettingsModel(IReadOnlyList<SettingDef> defs, IFileStore store, double revertSeconds = 15)
    {
        _defs = defs.ToList();
        _store = store;
        _revertSeconds = revertSeconds;
        foreach (var def in _defs)
        {
            def.Validate();
            if (!_byId.TryAdd(def.Id, def)) throw new ArgumentException($"Duplicate setting id {def.Id}.");
            _applied[def.Id] = def.Default;
        }
        LoadResult = Load();
        foreach (var def in _defs) _pending[def.Id] = _applied[def.Id];
    }

    public event Action<IReadOnlyList<string>>? Applied;

    public IReadOnlyList<SettingDef> Definitions => _defs;

    public SettingsLoadResult LoadResult { get; }

    public bool LastSaveFailed { get; private set; }

    public bool RevertPending => _revertTo is not null;

    public int RevertSecondsLeft => _countdown.WholeSecondsLeft;

    public IReadOnlyList<SettingDef> InCategory(SettingCategory category) => _defs.Where(d => d.Category == category).ToList();

    public SettingValue Value(string id) => _pending[id];

    public SettingValue AppliedValue(string id) => _applied[id];

    public bool Set(string id, SettingValue value)
    {
        if (RevertPending || !_byId.TryGetValue(id, out var def) || !def.TryNormalize(value, out var normalized)) return false;
        _pending[id] = normalized;
        return true;
    }

    public bool IsDirty(string id) => _pending[id] != _applied[id];

    public bool IsDirty(SettingCategory? scope = null) => _defs.Any(d => InScope(d, scope) && IsDirty(d.Id));

    public bool IsAtDefaults(SettingCategory? scope = null) => _defs.All(d => !InScope(d, scope) || _pending[d.Id] == d.Default);

    public IReadOnlyList<string> Apply(SettingCategory? scope = null)
    {
        var changed = new List<string>();
        if (RevertPending) return changed;
        Dictionary<string, SettingValue>? risky = null;
        foreach (var def in _defs.Where(d => InScope(d, scope) && IsDirty(d.Id)).ToList())
        {
            if (def.Risky) (risky ??= new())[def.Id] = _applied[def.Id];
            _applied[def.Id] = _pending[def.Id];
            changed.Add(def.Id);
        }
        if (changed.Count == 0) return changed;
        if (risky is not null)
        {
            _revertTo = risky;
            _countdown.Start(_revertSeconds);
        }
        Persist();
        Applied?.Invoke(changed);
        return changed;
    }

    public int Revert(SettingCategory? scope = null) => Assign(scope, d => _applied[d.Id]);

    public int ResetToDefaults(SettingCategory? scope = null) => Assign(scope, d => d.Default);

    public bool Tick(double deltaSeconds)
    {
        if (_revertTo is null || !_countdown.Tick(deltaSeconds)) return false;
        RevertRisky();
        return true;
    }

    public void KeepChanges()
    {
        if (_revertTo is null) return;
        _revertTo = null;
        _countdown.Stop();
        Persist();
    }

    public void RevertRisky()
    {
        if (_revertTo is not { } undo) return;
        _revertTo = null;
        _countdown.Stop();
        foreach (var (id, value) in undo)
        {
            _applied[id] = value;
            _pending[id] = value;
        }
        Persist();
        Applied?.Invoke(undo.Keys.ToList());
    }

    public MenuList<SettingDef> CreateMenuList(SettingCategory? scope = null) =>
        new(_defs.Where(d => InScope(d, scope)).ToList(), d => d.Id, d => d.SearchText, d => d.Category.ToString());

    private static bool InScope(SettingDef def, SettingCategory? scope) => scope is null || def.Category == scope;

    private int Assign(SettingCategory? scope, Func<SettingDef, SettingValue> target)
    {
        if (RevertPending) return 0;
        var count = 0;
        foreach (var def in _defs.Where(d => InScope(d, scope)))
        {
            var value = target(def);
            if (_pending[def.Id] == value) continue;
            _pending[def.Id] = value;
            count++;
        }
        return count;
    }

    private void Persist()
    {
        var values = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var def in _defs)
        {
            var value = _revertTo is not null && _revertTo.TryGetValue(def.Id, out var old) ? old : _applied[def.Id];
            values[def.Id] = def.Kind switch
            {
                SettingKind.Toggle => (object)value.Flag,
                SettingKind.Slider => value.Number,
                _ => value.Text,
            };
        }
        try
        {
            LastSaveFailed = !_store.WriteAllText(FileName, JsonSerializer.Serialize(new { version = 1, values }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LastSaveFailed = true;
        }
    }

    private SettingsLoadResult Load()
    {
        string? text;
        try
        {
            text = _store.ReadAllText(FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return SettingsLoadResult.Corrupt;
        }
        if (text is null) return SettingsLoadResult.NoFile;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("values", out var values) ||
                values.ValueKind != JsonValueKind.Object)
                return SettingsLoadResult.Corrupt;
            foreach (var prop in values.EnumerateObject())
            {
                if (_byId.TryGetValue(prop.Name, out var def) && Read(def, prop.Value) is { } value &&
                    def.TryNormalize(value, out var normalized))
                    _applied[def.Id] = normalized;
            }
            return SettingsLoadResult.Loaded;
        }
        catch (JsonException)
        {
            return SettingsLoadResult.Corrupt;
        }
    }

    private static SettingValue? Read(SettingDef def, JsonElement element)
    {
        switch (def.Kind)
        {
            case SettingKind.Toggle when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return SettingValue.Toggle(element.GetBoolean());
            case SettingKind.Slider when element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number):
                return SettingValue.Slider(number);
            case SettingKind.Choice when element.ValueKind == JsonValueKind.String:
                return SettingValue.Choice(element.GetString() ?? "");
            case SettingKind.Keybinding when element.ValueKind == JsonValueKind.String:
                return SettingValue.Binding(element.GetString() ?? "");
            default:
                return null;
        }
    }
}
