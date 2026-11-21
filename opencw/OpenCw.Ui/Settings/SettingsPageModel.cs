using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Settings;

public sealed record SettingsRow(string Key, SettingCategory Category, string Label, string SearchText, SettingKind Kind)
{
    public SettingDef? Def { get; init; }

    public BindableAction? Action { get; init; }

    public InputDevice Device { get; init; }

    public bool IsBinding => Action is not null;
}

public sealed class SettingsPageModel
{
    private readonly Dictionary<string, BindingSlot> _slots = new();

    public SettingsPageModel(SettingsModel settings, KeyBindingModel keys)
    {
        Settings = settings;
        Keys = keys;
        var rows = new List<SettingsRow>();
        foreach (var category in Enum.GetValues<SettingCategory>())
        {
            foreach (var def in settings.InCategory(category))
                rows.Add(new SettingsRow(def.Id, category, def.Label, def.SearchText, def.Kind) { Def = def });
            if (category != SettingCategory.Controls) continue;
            foreach (var action in keys.Actions)
            {
                foreach (var device in Enum.GetValues<InputDevice>())
                {
                    rows.Add(new SettingsRow($"bind:{action.Id}:{device}", category, $"{action.Label} ({device})",
                        $"{action.Label} {device} key binding rebind {category}", SettingKind.Keybinding)
                    { Action = action, Device = device });
                }
            }
        }
        Rows = rows;
    }

    public SettingsModel Settings { get; }

    public KeyBindingModel Keys { get; }

    public IReadOnlyList<SettingsRow> Rows { get; }

    public bool IsListening => Keys.State == CaptureState.Listening;

    public bool IsResolving => Keys.State == CaptureState.Resolving;

    public InputDevice? CaptureDevice => Keys.Target?.Device;

    public string CaptureText
    {
        get
        {
            if (Keys.Target is not { } target) return string.Empty;
            var input = target.Device == InputDevice.Keyboard ? "key or mouse button" : "controller button";
            var clear = target.Device == InputDevice.Keyboard ? "Delete" : "View";
            return $"Press a {input} for {ActionLabel(target)}. Esc cancels, {clear} clears.";
        }
    }

    public string ConflictText => Keys.PendingCode is { } code
        ? $"{code} is already used by {string.Join(", ", Keys.Conflicts.Select(ActionLabel))}."
        : string.Empty;

    public static SettingCategory? ScopeOf(string? tab) =>
        Enum.TryParse<SettingCategory>(tab, out var category) && Enum.IsDefined(category) ? category : null;

    public static bool IsActionTab(IReadOnlyList<string> tabs, int index) => index >= 0 && index < tabs.Count - 1;

    public static SettingCategory? ScopeOfTab(IReadOnlyList<string> tabs, int index) =>
        IsActionTab(tabs, index) ? ScopeOf(tabs[index]) : null;

    public static string ScopeName(SettingCategory? scope) => scope?.ToString() ?? "all";

    public string DisplayLabel(SettingsRow row) => IsDirty(row) ? "* " + row.Label : row.Label;

    public bool IsDirty(SettingsRow row) => row.Def is { } def && Settings.IsDirty(def.Id);

    public BindingSlot Slot(SettingsRow row) => _slots.TryGetValue(row.Key, out var slot) ? slot : BindingSlot.Primary;

    public string Value(SettingsRow row)
    {
        if (row.Action is { } action) return BindingText(row, action);
        var def = row.Def!;
        var value = Settings.Value(def.Id);
        return def.Kind switch
        {
            SettingKind.Toggle => value.Flag ? "On" : "Off",
            SettingKind.Choice => def.Choices.FirstOrDefault(c => c.Id == value.Text)?.Label ?? value.Text,
            SettingKind.Slider => NumberText.Format(value.Number),
            _ => value.Text.Length == 0 ? "None" : value.Text,
        };
    }

    public bool SetSlider(SettingsRow row, double value) =>
        row.Def is { Kind: SettingKind.Slider } def && Change(def, SettingValue.Slider(value));

    public bool SetToggle(SettingsRow row, bool on) =>
        row.Def is { Kind: SettingKind.Toggle } def && Change(def, SettingValue.Toggle(on));

    public bool SetChoice(SettingsRow row, string id) =>
        row.Def is { Kind: SettingKind.Choice } def && Change(def, SettingValue.Choice(id));

    public bool Adjust(SettingsRow row, int direction)
    {
        if (direction == 0) return false;
        if (row.IsBinding)
        {
            var next = direction > 0 ? BindingSlot.Secondary : BindingSlot.Primary;
            if (Slot(row) == next) return false;
            _slots[row.Key] = next;
            return true;
        }
        if (row.Def is not { } def) return false;
        var value = Settings.Value(def.Id);
        switch (def.Kind)
        {
            case SettingKind.Toggle:
                return Change(def, SettingValue.Toggle(!value.Flag));
            case SettingKind.Choice:
                var index = Math.Max(0, def.Choices.ToList().FindIndex(c => c.Id == value.Text));
                var target = def.Choices[((index + Math.Sign(direction)) % def.Choices.Count + def.Choices.Count) % def.Choices.Count];
                return Change(def, SettingValue.Choice(target.Id));
            case SettingKind.Slider:
                var step = def.Step > 0 ? def.Step : (def.Max - def.Min) / 20;
                return Change(def, SettingValue.Slider(value.Number + Math.Sign(direction) * step));
            default:
                return false;
        }
    }

    public bool Activate(SettingsRow row)
    {
        if (row.IsBinding) return BeginCapture(row, Slot(row));
        return row.Def is { Kind: SettingKind.Toggle or SettingKind.Choice } && Adjust(row, 1);
    }

    public bool BeginCapture(SettingsRow row, BindingSlot slot)
    {
        if (row.Action is not { } action || Settings.RevertPending) return false;
        _slots[row.Key] = slot;
        return Keys.BeginCapture(action.Id, row.Device, slot);
    }

    public CaptureOutcome Capture(string code) => Keys.Capture(code);

    public void CancelCapture() => Keys.CancelCapture();

    public bool Resolve(ConflictResolution resolution) => Keys.Resolve(resolution);

    public void ClearCaptured()
    {
        if (Keys.Target is not { } target) return;
        Keys.CancelCapture();
        Keys.Unbind(target.ActionId, target.Device, target.Slot);
    }

    public void ClearBinding(SettingsRow row)
    {
        if (row.Action is { } action) Keys.Unbind(action.Id, row.Device, Slot(row));
    }

    public void Tick(double deltaSeconds)
    {
        Settings.Tick(deltaSeconds);
        Keys.Tick(deltaSeconds);
    }

    public string Notice
    {
        get
        {
            if (Settings.LastSaveFailed || Keys.LastSaveFailed)
                return "Your changes could not be saved. Apply retries the save.";
            if (Settings.LoadResult == SettingsLoadResult.Unreadable || Keys.LoadResult == SettingsLoadResult.Unreadable)
                return "Saved settings could not be read, so changes will not be saved this session.";
            if (Settings.LoadResult == SettingsLoadResult.Corrupt || Keys.LoadResult == SettingsLoadResult.Corrupt)
                return "A saved settings file was damaged and defaults are in use. The damaged file was kept with a .corrupt suffix.";
            return string.Empty;
        }
    }

    public bool IsDirty(SettingCategory? scope = null) =>
        Settings.IsDirty(scope) || (BindingsInScope(scope) && Keys.HasStagedReset) || Settings.LastSaveFailed || Keys.LastSaveFailed;

    public bool IsAtDefaults(SettingCategory? scope = null) =>
        Settings.IsAtDefaults(scope) && (!BindingsInScope(scope) || BindingsAtDefaults());

    public IReadOnlyList<string> Apply(SettingCategory? scope)
    {
        var changed = Settings.Apply(scope);
        if (BindingsInScope(scope))
            Keys.CommitStaged();
        else
            Keys.RetrySave();
        return changed;
    }

    public int Revert(SettingCategory? scope)
    {
        var count = Settings.Revert(scope);
        if (BindingsInScope(scope) && Keys.RevertStaged())
            count++;
        return count;
    }

    public int ResetToDefaults(SettingCategory? scope)
    {
        var count = Settings.ResetToDefaults(scope);
        if (BindingsInScope(scope) && !BindingsAtDefaults())
        {
            Keys.StageResetAll();
            count++;
        }
        return count;
    }

    private static bool BindingsInScope(SettingCategory? scope) => scope is null || scope == SettingCategory.Controls;

    private bool BindingsAtDefaults() =>
        Keys.Actions.All(a => Enum.GetValues<InputDevice>().All(d => Keys.IsDefault(a.Id, d)));

    private string ActionLabel(BindingRef reference) =>
        $"{Keys.Actions.First(a => a.Id == reference.ActionId).Label} ({reference.Device}, {reference.Slot})";

    private string BindingText(SettingsRow row, BindableAction action)
    {
        var primary = Keys.Get(action.Id, row.Device, BindingSlot.Primary) ?? "None";
        var secondary = Keys.Get(action.Id, row.Device, BindingSlot.Secondary) ?? "None";
        return Slot(row) == BindingSlot.Primary ? $"[{primary}] {secondary}" : $"{primary} [{secondary}]";
    }

    private bool Change(SettingDef def, SettingValue value)
    {
        var before = Settings.Value(def.Id);
        return Settings.Set(def.Id, value) && Settings.Value(def.Id) != before;
    }
}
