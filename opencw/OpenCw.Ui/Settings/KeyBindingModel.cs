using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenCommonwealth.Services.Ui.Settings;

public enum InputDevice
{
    Keyboard,
    Controller,
}

public enum BindingSlot
{
    Primary,
    Secondary,
}

public enum ConflictResolution
{
    Swap,
    Unbind,
    Cancel,
}

public enum CaptureState
{
    Idle,
    Listening,
    Resolving,
}

public enum CaptureOutcome
{
    Ignored,
    Bound,
    Conflict,
    Cancelled,
}

public sealed record BindableAction(string Id, string Label, string Context,
                                    string? KeyboardPrimary = null, string? KeyboardSecondary = null,
                                    string? ControllerPrimary = null, string? ControllerSecondary = null)
{
    public string? DefaultFor(InputDevice device, BindingSlot slot) => (device, slot) switch
    {
        (InputDevice.Keyboard, BindingSlot.Primary) => KeyboardPrimary,
        (InputDevice.Keyboard, BindingSlot.Secondary) => KeyboardSecondary,
        (InputDevice.Controller, BindingSlot.Primary) => ControllerPrimary,
        _ => ControllerSecondary,
    };
}

public sealed record BindingRef(string ActionId, InputDevice Device, BindingSlot Slot);

public sealed class KeyBindingModel
{
    public const string FileName = "keybindings.json";

    private static readonly BindingSlot[] Slots = { BindingSlot.Primary, BindingSlot.Secondary };

    private readonly List<BindableAction> _actions;
    private readonly Dictionary<string, BindableAction> _byId = new();
    private readonly Dictionary<BindingRef, string?> _bound = new();
    private readonly IFileStore _store;
    private double _captureLeft;

    public KeyBindingModel(IReadOnlyList<BindableAction> actions, IFileStore store)
    {
        _actions = actions.ToList();
        _store = store;
        foreach (var action in _actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id) || !_byId.TryAdd(action.Id, action))
                throw new ArgumentException($"Action id '{action.Id}' is empty or duplicated.");
        }
        SetAllToDefaults();
        LoadResult = Load();
    }

    public event Action? Changed;

    public IReadOnlyList<BindableAction> Actions => _actions;

    public SettingsLoadResult LoadResult { get; }

    public bool LastSaveFailed { get; private set; }

    public CaptureState State { get; private set; }

    public BindingRef? Target { get; private set; }

    public string? PendingCode { get; private set; }

    public IReadOnlyList<BindingRef> Conflicts { get; private set; } = Array.Empty<BindingRef>();

    public double CaptureTimeoutSeconds { get; set; } = 8;

    public string KeyboardCancelCode { get; set; } = "Escape";

    public double CaptureSecondsLeft => State == CaptureState.Listening ? Math.Max(_captureLeft, 0) : 0;

    public string? Get(string actionId, InputDevice device, BindingSlot slot) => _bound[new BindingRef(actionId, device, slot)];

    public bool IsDefault(string actionId, InputDevice device) =>
        Slots.All(slot => string.Equals(Get(actionId, device, slot), _byId[actionId].DefaultFor(device, slot), StringComparison.Ordinal));

    public IReadOnlyList<BindingRef> FindConflicts(string actionId, InputDevice device, BindingSlot slot, string code)
    {
        var context = _byId[actionId].Context;
        var self = new BindingRef(actionId, device, slot);
        return _bound
            .Where(kv => kv.Key != self && kv.Key.Device == device && kv.Value is not null &&
                         string.Equals(kv.Value, code, StringComparison.OrdinalIgnoreCase) &&
                         _byId[kv.Key.ActionId].Context == context)
            .Select(kv => kv.Key)
            .ToList();
    }

    public bool BeginCapture(string actionId, InputDevice device, BindingSlot slot)
    {
        if (!_byId.ContainsKey(actionId)) return false;
        Target = new BindingRef(actionId, device, slot);
        PendingCode = null;
        Conflicts = Array.Empty<BindingRef>();
        State = CaptureState.Listening;
        _captureLeft = CaptureTimeoutSeconds;
        return true;
    }

    public CaptureOutcome Capture(string code)
    {
        if (State != CaptureState.Listening || Target is not { } target || string.IsNullOrWhiteSpace(code))
            return CaptureOutcome.Ignored;
        if (target.Device == InputDevice.Keyboard && string.Equals(code, KeyboardCancelCode, StringComparison.OrdinalIgnoreCase))
        {
            CancelCapture();
            return CaptureOutcome.Cancelled;
        }
        var conflicts = FindConflicts(target.ActionId, target.Device, target.Slot, code);
        if (conflicts.Count == 0)
        {
            _bound[target] = code;
            EndCapture();
            Persist();
            return CaptureOutcome.Bound;
        }
        PendingCode = code;
        Conflicts = conflicts;
        State = CaptureState.Resolving;
        return CaptureOutcome.Conflict;
    }

    public void CancelCapture() => EndCapture();

    public bool Resolve(ConflictResolution resolution)
    {
        if (State != CaptureState.Resolving || Target is not { } target || PendingCode is not { } code) return false;
        if (resolution != ConflictResolution.Cancel)
        {
            var previous = _bound[target];
            for (var i = 0; i < Conflicts.Count; i++)
                _bound[Conflicts[i]] = resolution == ConflictResolution.Swap && i == 0 ? previous : null;
            _bound[target] = code;
            Persist();
        }
        EndCapture();
        return true;
    }

    public bool Tick(double deltaSeconds)
    {
        if (State != CaptureState.Listening || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return false;
        _captureLeft -= deltaSeconds;
        if (_captureLeft > 0) return false;
        EndCapture();
        return true;
    }

    public bool Unbind(string actionId, InputDevice device, BindingSlot slot)
    {
        if (!_byId.ContainsKey(actionId)) return false;
        _bound[new BindingRef(actionId, device, slot)] = null;
        Persist();
        return true;
    }

    public IReadOnlyList<BindingRef> Reset(string actionId, InputDevice device)
    {
        var displaced = new List<BindingRef>();
        var action = _byId[actionId];
        foreach (var slot in Slots)
        {
            var code = action.DefaultFor(device, slot);
            if (code is not null)
            {
                foreach (var holder in FindConflicts(actionId, device, slot, code).Where(h => h.ActionId != actionId))
                {
                    _bound[holder] = null;
                    displaced.Add(holder);
                }
            }
            _bound[new BindingRef(actionId, device, slot)] = code;
        }
        Persist();
        return displaced;
    }

    public void ResetDevice(InputDevice device)
    {
        foreach (var action in _actions)
            foreach (var slot in Slots) _bound[new BindingRef(action.Id, device, slot)] = action.DefaultFor(device, slot);
        Persist();
    }

    public void ResetAll()
    {
        SetAllToDefaults();
        Persist();
    }

    private void SetAllToDefaults()
    {
        foreach (var action in _actions)
            foreach (var device in Enum.GetValues<InputDevice>())
                foreach (var slot in Slots) _bound[new BindingRef(action.Id, device, slot)] = action.DefaultFor(device, slot);
    }

    private void EndCapture()
    {
        State = CaptureState.Idle;
        Target = null;
        PendingCode = null;
        Conflicts = Array.Empty<BindingRef>();
        _captureLeft = 0;
    }

    private void Persist()
    {
        object Section(InputDevice device) => _actions.ToDictionary(
            a => a.Id,
            a => new[] { Get(a.Id, device, BindingSlot.Primary), Get(a.Id, device, BindingSlot.Secondary) });
        try
        {
            LastSaveFailed = !_store.WriteAllText(FileName, JsonSerializer.Serialize(
                new { version = 1, keyboard = Section(InputDevice.Keyboard), controller = Section(InputDevice.Controller) },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LastSaveFailed = true;
        }
        Changed?.Invoke();
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
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return SettingsLoadResult.Corrupt;
            ReadSection(doc.RootElement, "keyboard", InputDevice.Keyboard);
            ReadSection(doc.RootElement, "controller", InputDevice.Controller);
            return SettingsLoadResult.Loaded;
        }
        catch (JsonException)
        {
            return SettingsLoadResult.Corrupt;
        }
    }

    private void ReadSection(JsonElement root, string name, InputDevice device)
    {
        if (!root.TryGetProperty(name, out var section) || section.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in section.EnumerateObject())
        {
            if (!_byId.ContainsKey(prop.Name) || prop.Value.ValueKind != JsonValueKind.Array || prop.Value.GetArrayLength() != 2) continue;
            var pair = prop.Value.EnumerateArray().ToArray();
            if (pair.Any(e => e.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))) continue;
            for (var i = 0; i < Slots.Length; i++)
            {
                var code = pair[i].ValueKind == JsonValueKind.String ? pair[i].GetString() : null;
                _bound[new BindingRef(prop.Name, device, Slots[i])] = string.IsNullOrWhiteSpace(code) ? null : code;
            }
        }
    }
}
