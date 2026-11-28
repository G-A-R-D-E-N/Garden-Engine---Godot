using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Settings;

public enum ControllerBindingKind
{
    None,
    Button,
    Axis,
}

public readonly record struct ControllerBinding(ControllerBindingKind Kind, string Name)
{
    public static ControllerBinding None => new(ControllerBindingKind.None, string.Empty);
}

public enum BindingEventKind
{
    None,
    Key,
    MouseButton,
    JoypadButton,
    JoypadMotion,
    Other,
}

public enum ObservedEvent
{
    Other,
    Key,
    MouseButton,
    JoypadButton,
    JoypadMotion,
}

public enum ManagedAxis
{
    None,
    TriggerLeft,
    TriggerRight,
    Other,
}

public readonly record struct PlannedBinding(BindingEventKind Kind, string Name, float AxisValue, int Device = -1);

public readonly record struct ObservedBinding(ObservedEvent Observed, ManagedAxis Axis = ManagedAxis.None);

public static class KeyBindingEvents
{
    public const string PadPrefix = "Pad ";

    public const string PadAxisPrefix = "Pad Axis ";

    public static ControllerBinding ParseController(string? code)
    {
        if (string.IsNullOrEmpty(code) || !code.StartsWith(PadPrefix, StringComparison.Ordinal))
            return ControllerBinding.None;
        if (code.StartsWith(PadAxisPrefix, StringComparison.Ordinal))
            return Named(ControllerBindingKind.Axis, code.Substring(PadAxisPrefix.Length));
        return Named(ControllerBindingKind.Button, code.Substring(PadPrefix.Length));
    }

    public static PlannedBinding Plan(InputMapEntry entry)
    {
        if (entry.Device == InputDevice.Controller)
        {
            ControllerBinding binding = ParseController(entry.Code);
            return binding.Kind switch
            {
                ControllerBindingKind.Button => new PlannedBinding(BindingEventKind.JoypadButton, binding.Name, 0f),
                ControllerBindingKind.Axis => new PlannedBinding(BindingEventKind.JoypadMotion, binding.Name, 1f),
                _ => new PlannedBinding(BindingEventKind.None, string.Empty, 0f),
            };
        }
        if (MouseName(entry.Code) is { } mouse)
            return new PlannedBinding(BindingEventKind.MouseButton, mouse, 0f);
        return string.IsNullOrEmpty(entry.Code) || entry.Code.Contains('+')
            ? new PlannedBinding(BindingEventKind.None, string.Empty, 0f)
            : new PlannedBinding(BindingEventKind.Key, entry.Code, 0f);
    }

    public static string? MouseName(string? code) => code switch
    {
        "Mouse Left" => "Left",
        "Mouse Right" => "Right",
        "Mouse Middle" => "Middle",
        "Mouse 4" => "Xbutton1",
        "Mouse 5" => "Xbutton2",
        _ => null,
    };

    public static ObservedEvent ObservedFor(string? className) => className switch
    {
        "InputEventKey" => ObservedEvent.Key,
        "InputEventMouseButton" => ObservedEvent.MouseButton,
        "InputEventJoypadButton" => ObservedEvent.JoypadButton,
        "InputEventJoypadMotion" => ObservedEvent.JoypadMotion,
        _ => ObservedEvent.Other,
    };

    public static ManagedAxis AxisFor(string? axisName) => axisName switch
    {
        "TriggerLeft" => ManagedAxis.TriggerLeft,
        "TriggerRight" => ManagedAxis.TriggerRight,
        _ => ManagedAxis.Other,
    };

    public static bool IsManaged(InputDevice device, ObservedEvent observed, ManagedAxis axis = ManagedAxis.None) =>
        device == InputDevice.Keyboard
            ? observed is ObservedEvent.Key or ObservedEvent.MouseButton
            : observed == ObservedEvent.JoypadButton ||
              (observed == ObservedEvent.JoypadMotion && IsManagedAxis(axis));

    public static bool TryParseDefined<T>(string? name, T excluded, out T value) where T : struct, Enum =>
        Enum.TryParse(name, out value) && Enum.IsDefined(value) && !EqualityComparer<T>.Default.Equals(value, excluded);

    public static bool IsManagedAxis(ManagedAxis axis) => axis is ManagedAxis.TriggerLeft or ManagedAxis.TriggerRight;

    public static IReadOnlyList<int> ManagedIndexes(InputDevice device, IReadOnlyList<ObservedBinding> events)
    {
        var managed = new List<int>();
        for (var index = 0; index < events.Count; index++)
            if (IsManaged(device, events[index].Observed, events[index].Axis))
                managed.Add(index);
        return managed;
    }

    public static IReadOnlyList<PlannedBinding> Creations(string actionName, InputDevice device, IReadOnlyList<InputMapEntry> entries)
    {
        var planned = new List<PlannedBinding>();
        foreach (var entry in entries)
        {
            if (entry.Device != device || !string.Equals(entry.ActionName, actionName, StringComparison.Ordinal))
                continue;
            var binding = Plan(entry);
            if (binding.Kind != BindingEventKind.None)
                planned.Add(binding);
        }
        return planned;
    }

    private static ControllerBinding Named(ControllerBindingKind kind, string name) =>
        name.Length == 0 ? ControllerBinding.None : new ControllerBinding(kind, name);
}
